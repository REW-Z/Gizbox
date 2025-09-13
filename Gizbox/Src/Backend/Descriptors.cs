using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

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
    /// 变量活跃信息  
    /// </summary>
    public class LiveInfo
    {
        public SymbolTable.Record variable;
        public List<(int def, int die)> liveRanges;
        public List<(int start, int die)> mergedRanges;

        public LiveInfo(SymbolTable.Record variable)
        {
            this.variable = variable;
            this.liveRanges = new();
        }

        public void AddRange(IEnumerable<(int def, int die)> ranges)
        {
            this.liveRanges.AddRange(ranges);
        }

        public void MergeRanges()
        {
            liveRanges.Sort((a, b) => a.def - b.def);
            List<(int def, int die)> merged = new();
            foreach(var r in liveRanges)
            {
                if(merged.Count == 0)
                {
                    merged.Add(r);
                    continue;
                }

                var last = merged[merged.Count - 1];
                //有交集  
                if(Math.Max(r.def, last.def) <= Math.Min(r.die, last.die))
                {
                    var newStart = Math.Min(r.def, last.def);
                    var newEnd = Math.Max(r.die, last.die);
                    merged[merged.Count - 1] = (newStart, newEnd);
                }
                else
                {
                    merged.Add(r);
                }
            }
            mergedRanges = merged;
        }
    }

    /// <summary>
    /// 控制流图  
    /// </summary>
    public class ControlFlowGraph
    {
        public List<BasicBlock> blocks = new();
        public BasicBlock entryBlock;
        public BasicBlock exitBlock;

        public List<(BasicBlock src, BasicBlock dst)> edges = new();

        public Dictionary<SymbolTable.Record, LiveInfo> varialbeLiveInfos = new(); 

        public void AddEdge(BasicBlock src, BasicBlock dst)
        {
            edges.Add((src, dst));
            src.successors.Add(dst);
            dst.predecessors.Add(src);
        }
        public void CaculateAndMergeLiveInfos()
        {
            foreach(var b in blocks)
            {
                foreach(var (rec, ranges) in b.variableLiveRanges)
                {
                    if(varialbeLiveInfos.ContainsKey(rec) == false)
                        varialbeLiveInfos[rec] = new(rec);
                    varialbeLiveInfos[rec].AddRange(ranges);
                }
            }
            foreach(var (rec, info) in varialbeLiveInfos)
            {
                info.MergeRanges();
            }
        }
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

        //包含Label  
        public string hasLabel;

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

        //变量活跃区间
        public Dictionary<SymbolTable.Record, List<(int start, int end)>> variableLiveRanges = new();

        //计算活跃区间  
        public void CaculateLiveRanges()
        {
            HashSet<SymbolTable.Record> variables = new();
            foreach(var use in USE)
                variables.Add(use.Key);
            foreach(var def in DEF)
                variables.Add(def.Key);
            foreach(var _in in IN)
                variables.Add(_in);
            foreach(var _out in OUT)
                variables.Add(_out);


            List<(int def, int dead)> ranges = new();

            List<int> useLines = new();
            List<int> defLines = new();

            foreach(var va in variables)
            {
                ranges.Clear();
                useLines.Clear();
                defLines.Clear();

                if(this.USE.TryGetValue(va, out var tuse))
                    useLines.AddRange(tuse);
                if(this.DEF.TryGetValue(va, out var tdef))
                    defLines.AddRange(tdef);
                useLines.Sort();
                defLines.Sort();


                bool isLive = OUT.Contains(va);
                int deadPos = isLive ? (this.endIdx + 1) : -1;//最后一个use的后一行

                int useidx = useLines.Count - 1;
                int defidx = defLines.Count - 1;
                while(useidx >= 0 || defidx >= 0)
                {
                    int line = -1;
                    bool isDef = false;
                    bool isUse = false;
                    if(useidx >= 0)
                    {
                        line = useLines[useidx];
                        isUse = true;
                    }
                    if(defidx >= 0 && defLines[defidx] > line)
                    {
                        line = defLines[defidx];
                        isDef = true;
                    }
                    
                    //是DEF行（1行只能有一个DEF）  
                    if(isDef)
                    {
                        if(isLive)
                        {
                            ranges.Add((line, deadPos));
                            isLive = false;
                        }

                        //一行可能有多个def  
                        while(defidx >= 0 && defLines[defidx] == line)
                            defidx--;
                    }

                    //是USE行
                    if(isUse)
                    {
                        if(isLive == false)//是区间内最后一个use  
                        {
                            isLive = true;
                            deadPos = line + 1;
                        }
                        //一行可能有多个use   
                        while(useidx >= 0 && useLines[useidx] == line)
                            useidx--;
                    }
                }
                //依然活跃  
                if(isLive)
                {
                    ranges.Add((this.startIdx, deadPos));
                }

                //写入基本块  
                if(this.variableLiveRanges.TryGetValue(va, out var list) == false)
                {
                    var newlist = new List<(int start, int end)>();
                    this.variableLiveRanges.Add(va, newlist);
                    list = newlist;
                }
                list.AddRange(ranges);
                GixConsole.WriteLine($"va:{va.name} 添加了 {ranges.Count} 个区间");
            }
        }
    }




    /// <summary>
    /// 寄存器冲突图
    /// </summary>
    public class RegInterfGraph
    {
        public class Node
        {
            public RegInterfGraph graph;
            public SymbolTable.Record variable;
            public HashSet<Node> neighbors = new();
            public bool isRecoloredNode = false;
            public RegisterEnum assignedColor = RegisterEnum.Undefined;

            public Node(RegInterfGraph graph, SymbolTable.Record variable)
            {
                this.graph = graph;
                this.variable = variable;
            }
            public Node(RegInterfGraph graph, RegisterEnum precolored)
            {
                this.graph = graph;
                this.variable = null;
                this.isRecoloredNode = true;
                assignedColor = precolored;
            }

            public void AddEdge(Node target)
            {
                if(neighbors.Contains(target))
                    return;

                if(neighbors.Add(target))
                {
                    target.neighbors.Add(this);
                }
            }
        }

        public List<Node> allNodes = new();

        /// <summary> 添加变量节点 </summary>
        public Node AddVarNode(SymbolTable.Record rec)
        {
            var node = new Node(this, rec);
            allNodes.Add(node);
            return node;
        }

        /// <summary> 添加预着色节点 </summary>
        public Node AddPrecoloredNode(RegisterEnum assignedColor)
        {
            var node = new Node(this, assignedColor);
            allNodes.Add(node);
            return node;
        }

        public void Coloring(params RegisterEnum[] colors)
        {
            for(int i = 0; i < 10; ++i)
            {
                bool success = TryColoring(colors);
                if(success)
                {
                    break;
                }
                else
                {
                    //todo:尝试溢出  
                    break;
                }
            }
        }
        private bool TryColoring(params RegisterEnum[] colors)
        {
            if(colors == null || colors.Length == 0)
                throw new GizboxException(ExceptioName.CodeGen, "TryColoring: colors 为空。");

            // 排序：按度数降序；预着色节点放前面（保持其 assignedColor）
            var orderedNodes = allNodes
                .OrderByDescending(n => n.isRecoloredNode ? int.MaxValue : n.neighbors.Count)
                .ThenByDescending(n => n.neighbors.Count)
                .ToList();

            foreach(var node in orderedNodes)
            {
                // 预着色节点  
                if(node.isRecoloredNode)
                {
                    continue;
                }

                // 邻居已用颜色集合
                HashSet<RegisterEnum> banned = new();
                foreach(var nb in node.neighbors)
                {
                    if(nb.assignedColor != RegisterEnum.Undefined)
                        banned.Add(nb.assignedColor);
                }

                // 选择第一个可用颜色
                RegisterEnum chosen = RegisterEnum.Undefined;
                foreach(var c in colors)
                {
                    if(!banned.Contains(c))
                    {
                        chosen = c;
                        break;
                    }
                }

                node.assignedColor = chosen;
                GixConsole.WriteLine((node.variable?.name ?? "?") + "着色为：" + chosen);

                //着色失败  
                if(chosen == RegisterEnum.Undefined)
                {
                    GixConsole.WriteLine("00000着色失败");
                    return false;
                }
            }

            //着色完毕  
            GixConsole.WriteLine("00000着色成功");
            return true;
        }
    }
}
