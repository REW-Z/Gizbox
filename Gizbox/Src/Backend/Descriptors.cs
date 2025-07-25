using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox.Src.Backend
{
    /// <summary>
    /// 变量位置
    /// </summary>
    public struct VarPos
    {
        public enum PosType
        {
            Reg,
            Mem
        }
        public PosType posType;
        public long addr;//mem addr or register idx  
    }

    /// <summary>
    /// 变量描述符
    /// </summary>
    public class VariableDescriptor//存放在符号表中
    {
        public List<VarPos> positions = new();
    }

    /// <summary>
    /// 寄存器描述符
    /// </summary>
    public class RegisterDescriptor
    {
        public List<VariableDescriptor> variables = new();
    }



    /// <summary>
    /// 控制流图  
    /// </summary>
    public class ControlFlowGraph
    {
        public List<BasicBlock> blocks;
        public BasicBlock entryBlock;
        public BasicBlock exitBlock;
    }
    /// <summary>
    /// 基本块
    /// </summary>
    public class BasicBlock
    {
        //起始行
        public int startIdx;
        //结束行
        public int endIdx;

        //前置节点
        public List<BasicBlock> predecessors = new List<BasicBlock>();
        //后继节点
        public List<BasicBlock> successors = new List<BasicBlock>();

        //变量使用信息
        public Dictionary<SymbolTable.Record, List<int>> USE = new();
        //变量定义/赋值信息
        public Dictionary<SymbolTable.Record, List<int>> DEF = new();

        //入口处活跃变量
        public List<SymbolTable.Record> IN = new();
        //出口处活跃变量
        public List<SymbolTable.Record> OUT = new();
    }



    /// <summary>
    /// 寄存器冲突图
    /// </summary>
    public class RegInterfGraph
    {
    }
}
