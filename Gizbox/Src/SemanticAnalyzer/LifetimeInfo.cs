using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Gizbox;


/// <summary>
/// 生命周期语义信息  
/// </summary>0.
public class LifetimeInfo
{
    public enum VarStatus
    {
        Alive = 1,
        Dead = 0,  //已Move或者Drop
        PossiblyDead = -1, //可能已Move或者Drop
    }

    public class Branch
    {
        public Stack<ScopeInfo> scopeStack = new();
    }

    public class ScopeInfo
    {
        public Dictionary<string, VarStatus> localVariableStatusDict = new();
    }

    public static VarStatus Meet(VarStatus a, VarStatus b)
    {
        if(a == b)
            return a;
        return VarStatus.PossiblyDead;
    }
    public static VarStatus MeetMany(IEnumerable<VarStatus> statuses)
    {
        using(var it = statuses.GetEnumerator())
        {
            if(!it.MoveNext())
                return VarStatus.Dead;
            VarStatus acc = it.Current;
            while(it.MoveNext())
                acc = Meet(acc, it.Current);
            return acc;
        }
    }

    public static bool IsUsable(VarStatus s)
    {
        return s == VarStatus.Alive;
    }


    // ------------ Instance ---------------

    public Branch mainBranch = new Branch();

    public Branch currBranch = null;

    public SymbolTable.RecordFlag currentFuncReturnFlag = SymbolTable.RecordFlag.None;

    public List<SymbolTable.Record> currentFuncParams = null;

    public Branch NewBranch(Branch srcBranch)
    {
        Branch newBranch = new Branch();

        //仅克隆最上层作用域  
        newBranch.scopeStack.Clear();
        foreach(var s in srcBranch.scopeStack.Reverse())
        {
            newBranch.scopeStack.Push(s);
        }
        var top = newBranch.scopeStack.Pop();
        var topClone = new ScopeInfo();
        foreach(var (k, v) in top.localVariableStatusDict)
        {
            topClone.localVariableStatusDict.Add(k, v);
        }
        newBranch.scopeStack.Push(topClone);

        return newBranch;
    }

    public void MergeBranchesTo(Branch mainBranch, IEnumerable<Branch> branches)
    {
        //合并检查  
        int depth = -1;
        string name = null;
        foreach(var b in branches)
        {
            int d = b.scopeStack.Count;
            if(depth == -1)
            {
                depth = d;
                name = null;//todo
            }

            if(depth != d || name != null)
                throw new GizboxException(ExceptioName.OwnershipError, "branch merge error.");
        }

        //合并变量状态    
        foreach(var (varname, varstatus) in mainBranch.scopeStack.Peek().localVariableStatusDict)
        {
            VarStatus finalStatus = varstatus;
            foreach(var b in branches)
            {
                var s = b.scopeStack.Peek().localVariableStatusDict[varname];
                finalStatus = Meet(finalStatus, s);
            }
            mainBranch.scopeStack.Peek().localVariableStatusDict[varname] = finalStatus;
        }
    }
}
