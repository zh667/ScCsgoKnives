using Engine;
using Game;
using Game.Network;

static partial class StateLoop {
    static readonly List<string> s_publicationTrace=[];
    sealed class PublicationTransport(IScNetTransport inner) : IScNetTransport, IScNetInventorySync {
        public ScNetRole Role=>inner.Role;public ScNetHandshake Handshake=>inner.Handshake;public string HandshakeDetail=>inner.HandshakeDetail;public IReadOnlyList<ScNetPeer> Peers=>inner.Peers;
        public Func<ScNetPeer,int,bool> Accept;public int Calls;
        public bool IsLocal(ComponentPlayer p)=>inner.IsLocal(p);
        public bool SendToServer(ushort op,byte[] payload)=>inner.SendToServer(op,payload);
        public bool SendTo(ScNetPeer p,ushort op,byte[] payload){
            if(op==ScNetMirror.OpRecords){int call=++Calls;s_publicationTrace.Add($"rows:{p.PlayerIndex}:{call}:{Convert.ToBase64String(payload)}");if(Accept?.Invoke(p,call)==false){s_publicationTrace.Add("rejected:"+call);return false;}}
            return inner.SendTo(p,op,payload);
        }
        public void Broadcast(ushort op,byte[] payload,ScNetPeer except)=>inner.Broadcast(op,payload,except);
        public bool Publish(IInventory i){s_publicationTrace.Add("slots:"+string.Join(',',Enumerable.Range(0,10).Select(i.GetSlotValue)));return ((IScNetInventorySync)inner).Publish(i);}
        public bool Correct(IInventory i,ScNetPeer p)=>((IScNetInventorySync)inner).Correct(i,p);
        public bool AnnounceActiveSlot(IInventory i,ScNetPeer p)=>((IScNetInventorySync)inner).AnnounceActiveSlot(i,p);
    }
    static void RunPublication() {
        foreach(string mode in new[]{"normal","observer-peer-false","first-batch-false","second-peer-false","second-batch-throw","record-rollback"}) {
            double now=8000;ScNet.Clock=()=>now;s_adapterType.GetField("Now").SetValue(null,(Func<double>)(()=>now));
            ScNetSlots.Clear();ScNetGuns.WorldClosed();
            var server=Build(mode+" server");var client=Build(mode+" A");var observer=Build(mode+" B");Join(server,client,observer);
            var underlying=ScNet.Transport;var transport=new PublicationTransport(underlying);ScNet.Attach(transport);s_publicationTrace.Add("case:"+mode);
            try {
                Enter(server,true);ScNetMirror.ReleaseRegistry(server.Registry);
                // 256 existing rows + the actually materialized shot record force two batches per peer.
                for(int i=0;i<256;i++)server.Registry.Allocate(s_ak,7,false,600,1500);
                int fresh=Fresh(s_ak);foreach(var w in new[]{server,client,observer}){w.Inventory(1).m_slots[0]=fresh;w.Inventory(1).ActiveSlotIndex=0;}
                Enter(client,false);ScNetGuns.Observe(client.Player[1]);ScNetGuns.PredictShot(client.Player[1]);
                ScNetGuns.SendInput(client.Player[1],true,true,true,false,false,false,0,Aim,true,false);ToServer(server);
                var remote=ScNetGuns.RemoteInput(server.Player[1]);
                var tx=ScGunMutation.Prepare(server.Inventory(1),0,ScGunHolders.PlayerKey(server.Player[1],0),out var why);
                if(mode=="record-rollback")typeof(ScGunMutation).GetField("AfterRecordWrite",All).SetValue(tx,(Action)(()=>throw new Exception("after actual record write")));
                int before=server.Registry.Count;var result=tx.Commit(r=>r.Rounds=Math.Max(0,r.Rounds-1));
                if(result==ScGunResult.Success)ScNetGuns.ServerShot(server.Player[1]);else ScNetGuns.ServerSettle(server.Player[1],false);
                Test("PUB",mode+" actual commit creates correct fired/skipped source",mode=="record-rollback"?result!=ScGunResult.Success&&remote.Fired==0&&remote.Skipped==1&&server.Registry.Count==before:result==ScGunResult.Success&&remote.Fired==1&&server.Registry.Count==before+1);
                s_publicationTrace.Add($"commit:{result}:epoch={ScInventoryTransaction.Revision(server.Inventory(1))}:due={ScNetSlots.PublicationDue}:fired={remote.Fired}:skipped={remote.Skipped}:lock={ScGunMutation.IsCommitting}");
                // Join registers observer P2 before acting P1: P1 is the second peer, batches 3/4.
                if(mode=="observer-peer-false")transport.Accept=(p,n)=>p.PlayerIndex!=2||n!=1;
                if(mode=="first-batch-false")transport.Accept=(p,n)=>p.PlayerIndex!=1||n!=3;
                if(mode=="second-peer-false")transport.Accept=(p,n)=>p.PlayerIndex!=1||n!=4;
                if(mode=="second-batch-throw")transport.Accept=(p,n)=>n==2?throw new IOException("second record batch"):true;
                Take();ScNetSlots.EndOfFrame();var packets=Take();
                var rowPackets=packets.Where(p=>IsCs(p)&&Op(p)==ScNetMirror.OpRecords).ToList();
                int slotIndex=packets.FindIndex(p=>p is InventorySyncPacket);
                Test("PUB",mode+" record packets precede slots and false differs from throw",mode=="second-batch-throw"?slotIndex==-1&&transport.Calls==2:slotIndex>0&&packets.Take(slotIndex).All(p=>p is not InventorySyncPacket));
                bool actingPeerUnconfirmed=mode is "first-batch-false" or "second-peer-false" or "second-batch-throw";
                Test("PUB",mode+" only whole-peer enqueue clears acknowledgement",remote.AckDue==actingPeerUnconfirmed);
                var sent=(System.Collections.IDictionary)typeof(ScNetMirror).GetField("s_sentRevisions",All).GetValue(null);
                Test("PUB",mode+" sent revision cache waits for every applicable peer",mode.Contains("false")||mode.Contains("throw")?sent.Count==0:sent.Count==server.Registry.Count);
                s_publicationTrace.Add($"first:attempts={transport.Calls}:cached={sent.Count}:ackDue={remote.AckDue}:slots={packets.Count(p=>p is InventorySyncPacket)}:due={ScNetSlots.PublicationDue}");
                if(mode=="normal"){
                    Deliver(rowPackets,client,s_sessionA);ScNetGuns.Observe(client.Player[1]);
                    Test("PUB","record before fresh slot defers actual committed ack",ScNetGuns.PendingShots(client.Player[1])==1&&client.Slot(1,0)==fresh&&client.Registry.Count==257);
                }
                Deliver(packets,client,s_sessionA);Deliver(packets,observer,s_sessionB);Enter(server,true);
                transport.Accept=null;int committedCount=server.Registry.Count;ScNetMirror.FlushRecords();var retry=Take();
                Deliver(retry,client,s_sessionA);Deliver(retry,client,s_sessionA);Deliver(retry,observer,s_sessionB);
                Enter(server,true);
                Test("PUB",mode+" record retransmission never repeats transaction",server.Registry.Count==committedCount&&remote.Fired==(mode=="record-rollback"?0:1)&&!remote.AckDue);
                if(mode=="second-batch-throw"){
                    Test("PUB","throw drops original slot batch under preserved baseline semantics",client.Slot(1,0)==fresh&&!ScNetSlots.PublicationDue);
                    ScNetSlots.Correct(server.Inventory(1),ScNet.Peers.First(p=>p.PlayerIndex==1));Deliver(Take(),client,s_sessionA);Enter(server,true);
                }
                Enter(client,false);ScNetGuns.Observe(client.Player[1]);
                Test("PUB",mode+" client final inventory and prediction match committed or restored state",client.Slot(1,0)==server.Slot(1,0)&&ScNetGuns.PendingShots(client.Player[1])==0);
                s_publicationTrace.Add($"client:item={client.Slot(1,0)}:pending={ScNetGuns.PendingShots(client.Player[1])}:records={client.Registry.Count}");
                if(mode=="normal"){
                    ScNetGuns.PredictShot(client.Player[1]);now+=.1;ScNetGuns.SendInput(client.Player[1],true,false,true,false,false,false,0,Aim,true,false);ToServer(server);
                    ScNetGuns.ServerSettle(server.Player[1],false);int rowCalls=transport.Calls;ScNetMirror.FlushRecords();var onlyAck=Take();
                    var records=onlyAck.Where(p=>IsCs(p)&&Op(p)==ScNetMirror.OpRecords).ToArray();
                    Test("PUB","no changed records still publishes actual refused shot confirmation",records.Length==1&&new ScNetReader((byte[])records[0].GetType().GetField("Payload").GetValue(records[0])).Int()==0&&remote.Skipped==1&&remote.Fired==1);
                    Deliver(onlyAck,client,s_sessionA);Deliver(onlyAck,client,s_sessionA);ScNetGuns.Observe(client.Player[1]);Test("PUB","duplicate zero-row acknowledgement settles once",ScNetGuns.PendingShots(client.Player[1])==0);
                    client.Inventory(1).m_slots[1]=Fresh(s_glock);client.Inventory(1).ActiveSlotIndex=1;ScNetGuns.Observe(client.Player[1]);ScNetGuns.PredictShot(client.Player[1]);
                    Deliver(onlyAck,client,s_sessionA);Test("PUB","late former selection cannot settle new prediction",ScNetGuns.PendingShots(client.Player[1])==1);
                    ScNetGuns.WorldClosed();Deliver(onlyAck,client,s_sessionA);Test("PUB","closed world does not resurrect prediction",ScNetGuns.PendingShots(client.Player[1])==0);
                }
            } finally {ScNetSlots.Clear();ScNetGuns.WorldClosed();ScNet.Attach(underlying);}
        }
    }
}
