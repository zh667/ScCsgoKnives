using Game;
static class BoundaryRuleChecks {
    static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    public static void Run(Action<string,Action> check) {
        check("receipt/request-report-observed-distinct",()=>{
            var partial=ScInventoryWriteReceipt.Removal(4,4,9,7);Need(!partial.Exact&&partial.Observed==2&&partial.Reported==4,"requested count mistaken for observed");
            var dishonest=ScInventoryWriteReceipt.Removal(2,1,9,7);Need(!dishonest.Exact&&dishonest.Observed==2,"provider report ignored");
            Need(ScInventoryWriteReceipt.Removal(2,2,9,7).Exact,"exact remove refused");
            var add=ScInventoryWriteReceipt.Addition(4,0,2);Need(!add.Exact&&add.Reported is null&&add.Observed==2,"partial add lost");
        });
        check("slots/reference-identity-and-first-due-order",()=>{
            var q=new ScSlotPublicationQueue();var a=new Equal();var b=new Equal();q.Touch(a);q.Touch(a);q.Touch(b);
            Need(q.Untouch(a)&&!q.Untouch(a)&&q.Untouch(b),"equal stores merged");
            q.Enqueue(b);q.Enqueue(a);q.Enqueue(b);var due=q.TakeDue();
            Need(due.Length==2&&ReferenceEquals(due[0],b)&&ReferenceEquals(due[1],a)&&!q.HasDue,"due order/dedup");
            q.Enqueue(a);Need(due.Length==2&&q.HasDue,"snapshot not isolated from reentrant enqueue");q.Clear();Need(!q.HasDue&&!q.HasTouched,"clear");
        });
        check("slots/touched-is-not-due",()=>{
            var q=new ScSlotPublicationQueue();object a=new();q.Touch(a);Need(q.HasTouched&&!q.HasDue,"touch published mid-transaction");q.Enqueue(a);q.ClearTouched();Need(q.HasDue&&!q.HasTouched,"role cleanup erased due snapshot early");
        });
        check("rows/last-batch-ack-and-full-success",()=>{
            var rows=Enumerable.Range(1,257).Select(i=>(i,"row"+i)).ToList();var batch=new ScRecordBatches(rows,256);var ack=new ScShotAcknowledgement(5,100,2,1);
            var sent=new List<(int Count,bool Ack)>();int cleared=0;
            bool ok=batch.Send(ack,(r,a)=>{sent.Add((r.Count,a.HasValue));return true;},()=>cleared++);
            Need(ok&&cleared==1&&sent.SequenceEqual(new[]{(256,false),(1,true)}),"last batch confirmation changed");
            sent.Clear();cleared=0;ok=batch.Send(ack,(r,a)=>{sent.Add((r.Count,a.HasValue));return sent.Count!=1;},()=>cleared++);
            Need(!ok&&cleared==0&&sent.Count==2&&sent[1].Ack,"false short-circuited later batch or cleared ack");
        });
        check("rows/no-rows-with-ack-and-no-work-without",()=>{
            int sends=0,cleared=0;var batch=new ScRecordBatches([],256);
            Need(batch.Send(null,(_,_)=>{sends++;return true;},()=>cleared++)&&sends==0&&cleared==0,"empty send");
            Need(batch.Send(new(1,2,3,4),(r,a)=>{sends++;return r.Count==0&&a.HasValue;},()=>cleared++)&&sends==1&&cleared==1,"ack only lost");
        });
        check("rows/exception-keeps-ack-and-aborts-current-batch",()=>{
            int sends=0,cleared=0;var batch=new ScRecordBatches(Enumerable.Range(0,513).Select(i=>(i,"r")).ToList(),256);bool threw=false;
            try{batch.Send(new(1,2,3,4),(_,_)=>{sends++;throw new IOException("transport");},()=>cleared++);}catch(IOException){threw=true;}
            Need(threw&&sends==1&&cleared==0,"exception swallowed or confirmation cleared");
        });
        check("ack/selection-and-count-validity",()=>{
            Need(new ScShotAcknowledgement(3,1,5,2).ValidFor(3)&&!new ScShotAcknowledgement(3,1,5,2).ValidFor(4),"selection mixed");
            Need(!new ScShotAcknowledgement(3,1,-1,2).ValidFor(3)&&new ScShotAcknowledgement(3,1,5,2).Resolved==7,"ack count changed");
        });
    }
    sealed class Equal {public override bool Equals(object o)=>o is Equal;public override int GetHashCode()=>1;}
}
