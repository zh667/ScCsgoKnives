using System.Reflection;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// Independent fixture instances; exact production paths, reusable against old and candidate DLLs in separate processes.
static class InventoryBoundaryChecks {
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    internal static readonly List<string> Trace = [];
    internal class Inventory : IInventory {
        public string Name = "i"; public Project Project => null; public int SlotsCount => 5; public int VisibleSlotsCount { get; set; } = 5; public int ActiveSlotIndex { get; set; }
        public int[] Values = [100,900,0,0,0], Counts = [1,5,0,0,0];
        public Func<int,int,int,int> RemoveAmount, ReportAmount, AddAmount;
        public Action<int,int,int> Removed, Added;
        public Func<string> Extra = () => "";
        public override bool Equals(object obj) => obj is Inventory; public override int GetHashCode() => 1;
        public int GetSlotValue(int s) => Counts[s] > 0 ? Values[s] : 0;
        public int GetSlotCount(int s) => Counts[s]; public int GetSlotCapacity(int s,int v) => s==0?1:40;
        public int GetSlotProcessCapacity(int s,int v)=>0;
        void Record(string phase,int slot,int value,int requested,int reported) => Trace.Add($"{Name}:{phase}:{slot}:{value}:{requested}:{reported}:values={string.Join(',',Values)}:counts={string.Join(',',Counts)}:epoch={ScInventoryTransaction.Revision(this)}:locked={ScGunMutation.IsCommitting}:due={ScNetSlots.PublicationDue}:{Extra()}");
        public int RemoveSlotItems(int s,int n) {
            int value=Values[s], take=Math.Min(Counts[s],RemoveAmount?.Invoke(s,value,n)??n); Counts[s]-=take;
            int report=ReportAmount?.Invoke(s,value,take)??take;
            Record("remove",s,value,n,report); Removed?.Invoke(s,value,take); return report;
        }
        public void AddSlotItems(int s,int v,int n) {
            int add=AddAmount?.Invoke(s,v,n)??n;
            if(Counts[s]>0&&Values[s]!=v)throw new InvalidOperationException("occupied");
            if(add>0){Values[s]=v;Counts[s]+=add;} Record("add",s,v,n,add); Added?.Invoke(s,v,add);
        }
        public void ProcessSlotItems(int s,int v,int n,int p,out int rv,out int rn){rv=rn=0;}
        public void DropAllItems(Vector3 p){}
        public int Total(int v)=>Enumerable.Range(0,5).Where(s=>Values[s]==v).Sum(s=>Counts[s]);
    }
    sealed class Transport : IScNetTransport, IScNetInventorySync {
        ScNetRole m_role=ScNetRole.Host;
        public Func<string> ObserveRole;
        public Action RoleRead;
        public ScNetRole Role {get{RoleRead?.Invoke();if(ObserveRole is not null)Trace.Add("role:"+ObserveRole());return m_role;}set=>m_role=value;} public ScNetHandshake Handshake=>ScNetHandshake.Accepted; public string HandshakeDetail=>"fixture";
        public IReadOnlyList<ScNetPeer> Peers {get;set;}=[]; public bool IsLocal(ComponentPlayer p)=>true;
        public Func<IInventory,bool> Publishing; public Action Sending;
        public bool Publish(IInventory i){Trace.Add("publish:"+(i is Inventory f?f.Name:"creative")+":"+string.Join(',',Enumerable.Range(0,Math.Min(5,i.SlotsCount)).Select(s=>$"{i.GetSlotValue(s)}/{i.GetSlotCount(s)}")));return Publishing?.Invoke(i)??true;}
        public bool Correct(IInventory i,ScNetPeer p){Trace.Add("correct");return true;}
        public bool AnnounceActiveSlot(IInventory i,ScNetPeer p){Trace.Add("active");return true;}
        public bool SendToServer(ushort op,byte[] bytes)=>false;
        public bool SendTo(ScNetPeer p,ushort op,byte[] bytes){Trace.Add($"rows:{p.PlayerIndex}:{op}:{Convert.ToBase64String(bytes)}");Sending?.Invoke();return true;}
        public void Broadcast(ushort op,byte[] bytes,ScNetPeer except){}
    }
    sealed class NoInventoryTransport : IScNetTransport {
        public ScNetRole Role=>ScNetRole.Host;public ScNetHandshake Handshake=>ScNetHandshake.Accepted;public string HandshakeDetail=>"no inventory service";
        public IReadOnlyList<ScNetPeer> Peers=>[];public bool IsLocal(ComponentPlayer p)=>true;
        public bool SendToServer(ushort op,byte[] payload)=>false;public bool SendTo(ScNetPeer p,ushort op,byte[] payload)=>true;public void Broadcast(ushort op,byte[] payload,ScNetPeer except){}
    }
    static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    readonly record struct CommitObservation(string Stage, long Epoch, bool Locked, int Item, int Count, int Materials, bool Due);
    static CommitObservation Observe(string stage, Inventory inventory) => new(stage, ScInventoryTransaction.Revision(inventory),
        ScGunMutation.IsCommitting, inventory.GetSlotValue(0), inventory.GetSlotCount(0), inventory.Total(900), ScNetSlots.PublicationDue);
    static bool SaveRefusedDuringCommit(ScGunRegistry registry) {
        try { registry.Save(0); return false; }
        catch (InvalidOperationException e) { return e.Message.Contains("transaction/recovery"); }
    }
    static string Debt(ScGunRegistry r){var x=new XElement("R");r.Recovery.Save().Save(x);return x.ToString(SaveOptions.DisableFormatting);}
    sealed class Proxy : Inventory {
        public IInventory Backing;
        public bool Resolve(out IInventory storage){storage=Backing;return true;}
    }
    public static void Run(Action<string,Action> test) {
        void Case(string name,Action<Inventory,ScGunRegistry,Transport> body)=>test("inventory-boundary/"+name,()=>{
            var previous=ScGunRegistry.Current;var project=GameManager.m_project;var clock=ScNet.Clock;
            var i=new Inventory();var r=new ScGunRegistry{RecoveryOwner=_=>"player/0"};var net=new Transport();
            ScGunRegistry.Current=r;GameManager.m_project=new Project();ScNet.Clock=()=>100;ScNet.Attach(net);ScNetSlots.Clear();Trace.Add("case:"+name);
            try{body(i,r,net);Trace.Add($"end:epoch={ScInventoryTransaction.Revision(i)}:locked={ScGunMutation.IsCommitting}:due={ScNetSlots.PublicationDue}:debt={Debt(r)}");Need(!ScGunMutation.IsCommitting,"lock leaked");}
            finally{ScNetSlots.Clear();ScNet.Attach(null);ScGunRegistry.Current=previous;GameManager.m_project=project;ScNet.Clock=clock;}
        });
        bool Replace(Inventory i)=>ScInventoryTransaction.ReplaceWithCost(i,0,100,200,900,2);
        foreach(string fault in new[]{"normal","reject","partial-remove","reported-wrong","remove-throw","add-throw","owner-change","debt"}) Case("replace-"+fault,(i,r,n)=>{
            if(fault=="reject")i.AddAmount=(_,v,x)=>v==200?0:x;
            if(fault=="partial-remove")i.RemoveAmount=(_,v,x)=>v==900?1:x;
            if(fault=="reported-wrong")i.ReportAmount=(_,v,x)=>v==900?0:x;
            if(fault=="remove-throw")i.Removed=(_,v,_)=>{if(v==100)throw new Exception("removed then threw");};
            if(fault=="add-throw")i.Added=(_,v,_)=>{if(v==200)throw new Exception("added then threw");};
            if(fault=="owner-change")i.Added=(_,v,_)=>{if(v==200)r.RecoveryOwner=_=>"player/1";};
            if(fault=="debt")i.AddAmount=(_,_,_)=>0;
            bool result=Replace(i);Trace.Add("result:"+result);
            Need(result==(fault=="normal"),"outcome changed");
            if(fault=="normal")Need(i.Total(200)==1&&i.Total(900)==3,"normal totals");
            else if(fault is not("owner-change" or "debt"))Need(i.Total(100)==1&&i.Total(200)==0&&i.Total(900)==5,"inverse failed");
            else Need(r.Recovery.Count==1,"durable debt missing");
            ScNetSlots.EndOfFrame();
            if(fault is "debt" or "owner-change"){
                var saved=new XElement("R");r.Recovery.Save().Save(saved);var data=new ValuesDictionary();data.ApplyOverrides(XElement.Parse(saved.ToString()));var loaded=ScGunRecovery.Load(data);
                i.Added=null;i.AddAmount=(_,_,_)=>0;i.RemoveAmount=(_,_,_)=>0;long epoch=ScInventoryTransaction.Revision(i);loaded.Retry(_=>i);
                Need(ScInventoryTransaction.Revision(i)==epoch+1&&!ScNetSlots.PublicationDue,"no-progress retry epoch/touched semantics");
                i.RemoveAmount=null;i.AddAmount=(_,_,x)=>Math.Min(1,x);loaded.Retry(_=>i);ScNetSlots.EndOfFrame();i.AddAmount=null;
                loaded.Retry(_=>i);loaded.Retry(_=>i);ScNetSlots.EndOfFrame();
                Need(loaded.Count==0&&i.Total(100)==1&&i.Total(900)==5&&i.Total(200)==0,"saved compensation duplicate/lost");
            }
        });
        Case("nested-save-and-early-refusal",(i,r,n)=>{
            i.Removed=(_,_,_)=>{Need(ScGunMutation.IsCommitting,"released during inventory callback");Need(!Replace(i),"nested commit accepted");bool blocked=false;try{r.Save(0);}catch(InvalidOperationException){blocked=true;}Need(blocked,"save inside commit accepted");};
            Need(Replace(i),"outer failed");long epoch=ScInventoryTransaction.Revision(i);ScNetSlots.EndOfFrame();Need(!Replace(i)&&ScInventoryTransaction.Revision(i)==epoch&&!ScNetSlots.PublicationDue,"early refusal publishes");
        });
        Case("commit-notification-order-under-lock",(i,r,n)=>{
            long epoch=ScInventoryTransaction.Revision(i);
            var observed=new List<CommitObservation>(); bool written=false, saveRefused=false;
            // Capture inside production callbacks; assert only after return so a production catch cannot hide failures.
            i.Added=(slot,value,_)=>{if(slot==0&&value==200){written=true;observed.Add(Observe("written",i));}};
            n.RoleRead=()=>{if(written)observed.Add(Observe("notification",i));};
            n.ObserveRole=()=>$"epoch={ScInventoryTransaction.Revision(i)}:lock={ScGunMutation.IsCommitting}:item={i.GetSlotValue(0)}";
            ScNet.Clock=()=>{
                observed.Add(Observe("rewrite",i)); saveRefused=SaveRefusedDuringCommit(r);
                Trace.Add($"rewrite-clock:epoch={ScInventoryTransaction.Revision(i)}:locked={ScGunMutation.IsCommitting}:item={i.GetSlotValue(0)}");return 100;
            };
            bool result=Replace(i);
            n.RoleRead=null;n.ObserveRole=null;observed.Add(Observe("returned",i));
            n.Publishing=_=>{observed.Add(Observe("published",i));return true;};ScNetSlots.EndOfFrame();
            Need(result,"successful replacement refused");
            Need(observed.Count(o=>o.Stage=="written")==1&&observed.Count(o=>o.Stage=="rewrite")==1,"write/rewrite phase missing or duplicated");
            int rewrite=observed.FindIndex(o=>o.Stage=="rewrite"),returned=observed.FindIndex(o=>o.Stage=="returned");
            Need(observed[0].Stage=="written"&&rewrite>0&&returned>rewrite,"write/rewrite/return order changed");
            Need(observed.Take(returned).All(o=>o.Locked),"commit notification ran outside inventory mutex");
            Need(observed.Take(rewrite+1).All(o=>o.Epoch==epoch&&!o.Due),"epoch/publication advanced before SlotRewritten");
            var notifications=observed.Skip(rewrite+1).Take(returned-rewrite-1).ToArray();
            Need(notifications.Length>0&&notifications.All(o=>o.Stage=="notification"&&o.Epoch==epoch+1&&!o.Due),"TransactionEnded must observe the advanced epoch before enqueue");
            Need(observed.All(o=>o.Item==200&&o.Count==1&&o.Materials==3),"notification observed intermediate inventory");
            Need(saveRefused,"save accepted during SlotRewritten notification");
            Need(observed[returned]==new CommitObservation("returned",epoch+1,false,200,1,3,true),"return did not release mutex after publication registration");
            Need(observed.Last()==new CommitObservation("published",epoch+1,false,200,1,3,false)&&observed.Count(o=>o.Stage=="published")==1,"final frame publication missing, repeated or still locked");
        });
        Case("baseline-rewrite-clock-exception-rolls-back",(i,r,n)=>{
            long epoch=ScInventoryTransaction.Revision(i);bool rollingBack=false;
            var observed=new List<CommitObservation>();var saveRefusals=new List<bool>();
            i.Removed=(_,_,_)=>{if(rollingBack){observed.Add(Observe("undo-remove",i));saveRefusals.Add(SaveRefusedDuringCommit(r));}};
            i.Added=(_,_,_)=>{if(rollingBack){observed.Add(Observe("undo-add",i));saveRefusals.Add(SaveRefusedDuringCommit(r));}};
            n.RoleRead=()=>{if(rollingBack)observed.Add(Observe("notification",i));};
            ScNet.Clock=()=>{observed.Add(Observe("rewrite",i));rollingBack=true;throw new IOException("rewrite clock unavailable");};
            bool result=Replace(i);n.RoleRead=null;observed.Add(Observe("returned",i));
            ScNet.Clock=()=>100;n.Publishing=_=>{observed.Add(Observe("published",i));return true;};ScNetSlots.EndOfFrame();
            Need(!result&&i.Total(100)==1&&i.Total(900)==5&&i.Total(200)==0,"baseline notification exception boundary changed");
            int returned=observed.FindIndex(o=>o.Stage=="returned");
            Need(observed[0]==new CommitObservation("rewrite",epoch,true,200,1,3,false),"rollback began after notification lost mutex or advanced epoch");
            var undo=observed.Where(o=>o.Stage.StartsWith("undo-")).ToArray();
            Need(undo.SequenceEqual(new[]{
                new CommitObservation("undo-remove",epoch,true,0,0,3,false),
                new CommitObservation("undo-add",epoch,true,100,1,3,false),
                new CommitObservation("undo-add",epoch,true,100,1,5,false)
            }),"inverse compensation order, mutex or epoch changed");
            int lastUndo=observed.FindLastIndex(o=>o.Stage.StartsWith("undo-"));
            Need(observed.Take(returned).All(o=>o.Locked)&&saveRefusals.Count==3&&saveRefusals.All(blocked=>blocked),"compensation ran unlocked or allowed a save");
            var notifications=observed.Where(o=>o.Stage=="notification").ToArray();
            Need(notifications.Length>0&&observed.FindIndex(o=>o.Stage=="notification")>lastUndo
                &&notifications.All(o=>o.Epoch==epoch+1&&o.Item==100&&o.Count==1&&o.Materials==5&&!o.Due),"rollback advanced/published before inverse writes finished");
            Need(r.Recovery.Count==0&&observed[returned]==new CommitObservation("returned",epoch+1,false,100,1,5,true),"rollback return retained lock or lost final publication");
            Need(observed.Last()==new CommitObservation("published",epoch+1,false,100,1,5,false)&&observed.Count(o=>o.Stage=="published")==1,"rollback published an intermediate or locked state");
        });
        foreach(bool failure in new[]{false,true})Case("gun-record-"+failure,(i,r,n)=>{
            BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)]=701;
            int id=r.Allocate(0,7,false,600,1500);i.Values[0]=Terrain.MakeBlockValue(701,0,GunSpec.WithId(0,id));
            i.Extra=()=>r.NetworkRow(id,0);var before=r.NetworkRow(id,0);
            var tx=ScGunMutation.Prepare(i,0,ScGunHolders.Key(i,0),out var why);Need(tx is not null,why.ToString());
            typeof(ScGunMutation).GetField("AfterRecordWrite",Any).SetValue(tx,(Action)(()=>{Trace.Add($"record:{r.NetworkRow(id,0)}:epoch={ScInventoryTransaction.Revision(i)}:lock={ScGunMutation.IsCommitting}");if(failure)throw new Exception("after record");}));
            var result=tx.Commit(g=>g.Rounds=5,900,2);Trace.Add("gun-result:"+result);
            Need(failure? r.NetworkRow(id,0)==before&&i.Total(900)==5: r.TryGetSnapshot(id,out var snap)&&snap.Rounds==5&&i.Total(900)==3,"record rollback order");ScNetSlots.EndOfFrame();
        });
        foreach(bool fail in new[]{false,true})Case("batch-and-consume-"+fail,(i,r,n)=>{
            if(fail)i.AddAmount=(_,v,x)=>v==300?Math.Min(1,x):x;
            bool crafted=ScCraftBatch.TryCraft(i,300,new Dictionary<int,int>{{900,1}},2);Trace.Add("craft:"+crafted);
            Need(crafted==!fail,"batch partial add accepted");ScNetSlots.EndOfFrame();
            i.AddAmount=null;i.Values[2]=500;i.Counts[2]=1;bool consumed=ScCraftBatch.TryUseItem(i,2,500,()=>{Trace.Add("action:locked="+ScGunMutation.IsCommitting);return !fail;});Trace.Add("consume:"+consumed);
            Need(consumed==!fail&&i.Total(500)==(fail?1:0),"consume compensation");ScNetSlots.EndOfFrame();
        });
        Case("creative-replacement",(i,r,n)=>{
            var creative=new ComponentCreativeInventory{OpenSlotsCount=1};creative.m_slots.Add(100);
            Need(ScInventoryTransaction.ReplaceWithCost(creative,0,100,200,0,0)&&creative.GetSlotValue(0)==200,"creative replacement");ScNetSlots.EndOfFrame();
        });
        foreach(bool fail in new[]{false,true})Case("knife-finish-"+fail,(i,r,n)=>{
            // Isolate the commit boundary: the quote is already approved; no catalogue/UI changes under test.
            var quote=new ScKnifeSkinning.Quote(i,i,0,100,200,false,new Dictionary<int,int>{{900,2}});
            if(fail)i.Added=(_,v,_)=>{if(v==200)throw new Exception("knife write then throw");};
            bool result=ScKnifeSkinning.Apply(i,quote,false);Trace.Add("knife:"+result);
            Need(result==!fail&&i.Total(fail?100:200)==1&&i.Total(900)==(fail?5:3),"knife compensation/cost changed");ScNetSlots.EndOfFrame();
        });
        Case("proxy-resolution-dedup-and-retune",(i,r,n)=>{
            var resolvers=(System.Collections.IDictionary)typeof(ScInventoryIdentity).GetField("Resolvers",Any).GetValue(null);
            resolvers[typeof(Proxy)]=typeof(Proxy).GetMethod("Resolve");
            try{
                var a=new Proxy{Backing=i};var b=new Proxy{Backing=i};var other=new Inventory{Name="new-storage"};
                ScNetSlots.Touched(a);ScInventoryTransaction.Changed(b);ScNetSlots.Changed(a);int sent=ScNetSlots.Published;ScNetSlots.EndOfFrame();
                Need(ScNetSlots.Published==sent+1&&ScInventoryTransaction.Revision(a)==ScInventoryTransaction.Revision(b),"aliases not shared");
                i.Removed=(slot,_,_)=>{if(slot==0)a.Backing=other;};
                bool result=ScInventoryTransaction.ReplaceWithCost(a,0,100,200,900,2);Trace.Add("retune:"+result);
                Need(!result&&i.Total(100)==1&&i.Total(900)==5&&other.Total(100)==1&&other.Total(900)==5,"retune wrote/refunded wrong storage");
                Need(!ReferenceEquals(ScInventoryIdentity.Storage(a),ScInventoryIdentity.Storage(b)),"proxy mapping cached");ScNetSlots.EndOfFrame();
            }finally{resolvers.Remove(typeof(Proxy));}
        });
        Case("null-correction-active-and-unsupported",(i,r,n)=>{
            int sent=ScNetSlots.Published;ScNetSlots.EndOfFrame();ScNetSlots.Touched(null);ScNetSlots.Changed(null);
            Need(ScNetSlots.Published==sent&&!ScNetSlots.PublicationDue&&!ScNetSlots.Correct(i,null),"empty/null path changed");
            var peer=new ScNetPeer{PlayerIndex=1};Need(ScNetSlots.Correct(i,peer)&&ScNetSlots.ActiveSlotAdopted(i,peer),"correction/active path changed");
            ScNet.Attach(new NoInventoryTransport());int unsupported=ScNetSlots.Unsupported;ScNetSlots.Changed(i);ScNetSlots.Changed(i);
            Need(ScNetSlots.Unsupported==unsupported+2&&!ScNetSlots.PublicationDue&&!ScNetSlots.Correct(i,peer)&&!ScNetSlots.ActiveSlotAdopted(i,peer),"unsupported provider path changed");
        });
        Case("correction-flush-exception-propagates",(i,r,n)=>{
            n.Peers=[new ScNetPeer{PlayerIndex=1}];r.Allocate(0,5,false,600,1500);n.Sending=()=>throw new IOException("correction rows failure");
            int corrections=ScNetSlots.Corrections;bool threw=false;
            try{ScNetSlots.Correct(i,n.Peers[0]);}catch(IOException){threw=true;}
            Need(threw&&ScNetSlots.Corrections==corrections,"correction swallowed flush exception or counted unsent correction");
        });
        foreach(string mode in new[]{"normal","false","throw","reenter","clear","role","flush-throw","role-during","clear-during"})Case("publication-"+mode,(i,r,n)=>{
            var other=new Inventory{Name="j"};var third=new Inventory{Name="k"};
            ScNetSlots.Touched(i);ScNetSlots.Touched(i);ScInventoryTransaction.Changed(i);ScNetSlots.Changed(i);ScNetSlots.Changed(other);
            if(mode=="false")n.Publishing=x=>!ReferenceEquals(x,i);
            if(mode=="throw")n.Publishing=x=>throw new Exception("publish failure");
            if(mode=="reenter")n.Publishing=x=>{if(ReferenceEquals(x,i))ScNetSlots.Changed(third);return true;};
            if(mode=="role-during")n.Publishing=x=>{n.Role=ScNetRole.Client;return true;};
            if(mode=="clear-during")n.Publishing=x=>{ScNetSlots.Clear();return true;};
            if(mode=="clear")ScNetSlots.Clear();
            if(mode=="role")n.Role=ScNetRole.Client;
            if(mode=="flush-throw"){n.Peers=[new ScNetPeer{PlayerIndex=1}];r.Allocate(0,5,false,600,1500);n.Sending=()=>throw new Exception("rows failure");}
            int published=ScNetSlots.Published;ScNetSlots.EndOfFrame();int delta=ScNetSlots.Published-published;
            Need(delta==(mode is "throw" or "clear" or "role" or "flush-throw"?0:mode=="false"?1:2),"publication continue/abort/order changed");
            Need(ScNetSlots.PublicationDue==(mode=="reenter"),"snapshot drain/reentry changed");n.Publishing=null;n.Sending=null;ScNetSlots.EndOfFrame();
        });
    }
}
