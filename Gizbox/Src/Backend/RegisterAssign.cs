using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Gizbox.IR;

namespace Gizbox.Src.Backend
{
    /// <summary>
    /// 干扰图节点
    /// </summary>
    public class InterferenceNode
    {
        public SymbolTable.Record variable;
        public VariableDescriptor descriptor;
        public HashSet<InterferenceNode> neighbors = new HashSet<InterferenceNode>();
        public int color = -1; // -1表示未着色
        public bool isSpilled = false;

        public InterferenceNode(SymbolTable.Record var, VariableDescriptor desc)
        {
            variable = var;
            descriptor = desc;
        }

        public int Degree => neighbors.Count;

        public void AddNeighbor(InterferenceNode other)
        {
            if(other != this)
            {
                neighbors.Add(other);
                other.neighbors.Add(this);
            }
        }

        public void RemoveFromGraph()
        {
            foreach(var neighbor in neighbors)
            {
                neighbor.neighbors.Remove(this);
            }
            neighbors.Clear();
        }

        /// <summary>
        /// 检查变量是否已分配到寄存器
        /// </summary>
        public bool IsInRegister()
        {
            return descriptor.positions.Any(pos => pos.posType == VarPos.PosType.Reg);
        }

        /// <summary>
        /// 获取分配的寄存器
        /// </summary>
        public RegisterEnum? GetAssignedRegister()
        {
            var regPos = descriptor.positions.FirstOrDefault(pos => pos.posType == VarPos.PosType.Reg);
            return regPos.posType == VarPos.PosType.Reg ? (RegisterEnum)regPos.addr : null;
        }
    }

    /// <summary>
    /// 干扰图
    /// </summary>
    public class InterferenceGraph
    {
        public Dictionary<SymbolTable.Record, InterferenceNode> nodes = new Dictionary<SymbolTable.Record, InterferenceNode>();

        public InterferenceNode GetOrCreateNode(SymbolTable.Record variable, VariableDescriptor descriptor)
        {
            if(!nodes.ContainsKey(variable))
            {
                nodes[variable] = new InterferenceNode(variable, descriptor);
            }
            return nodes[variable];
        }

        public void AddInterference(SymbolTable.Record var1, SymbolTable.Record var2,
                                   VariableDescriptor desc1, VariableDescriptor desc2)
        {
            if(var1 == var2)
                return;

            var node1 = GetOrCreateNode(var1, desc1);
            var node2 = GetOrCreateNode(var2, desc2);
            node1.AddNeighbor(node2);
        }

        public List<InterferenceNode> GetAllNodes()
        {
            return nodes.Values.ToList();
        }

        public void RemoveNode(InterferenceNode node)
        {
            nodes.Remove(node.variable);
            node.RemoveFromGraph();
        }
    }

    /// <summary>
    /// 寄存器分配管理器
    /// </summary>
    public class RegisterAllocator
    {
        private readonly List<RegisterEnum> availableIntegerRegisters = new List<RegisterEnum>
        {
            RegisterEnum.RAX, RegisterEnum.RBX, RegisterEnum.RCX, RegisterEnum.RDX,
            RegisterEnum.RSI, RegisterEnum.RDI, RegisterEnum.R8, RegisterEnum.R9,
            RegisterEnum.R10, RegisterEnum.R11, RegisterEnum.R12, RegisterEnum.R13,
            RegisterEnum.R14, RegisterEnum.R15
        };

        private readonly List<RegisterEnum> availableFloatRegisters = new List<RegisterEnum>
        {
            RegisterEnum.XMM0, RegisterEnum.XMM1, RegisterEnum.XMM2, RegisterEnum.XMM3,
            RegisterEnum.XMM4, RegisterEnum.XMM5, RegisterEnum.XMM6, RegisterEnum.XMM7,
            RegisterEnum.XMM8, RegisterEnum.XMM9, RegisterEnum.XMM10, RegisterEnum.XMM11,
            RegisterEnum.XMM12, RegisterEnum.XMM13, RegisterEnum.XMM14, RegisterEnum.XMM15
        };

        private InterferenceGraph interferenceGraph;

        //描述符
        private Dictionary<SymbolTable.Record, VariableDescriptor> variableDescriptors = new Dictionary<SymbolTable.Record, VariableDescriptor>();
        private Dictionary<RegisterEnum, RegisterDescriptor> registerDescriptors = new Dictionary<RegisterEnum, RegisterDescriptor>();

        private int currentSpillOffset = 0;

        public RegisterAllocator()
        {
            interferenceGraph = new InterferenceGraph();
            InitializeRegisterDescriptors();
        }

        /// <summary>
        /// 初始化寄存器描述符
        /// </summary>
        private void InitializeRegisterDescriptors()
        {
            // 为每个可用寄存器创建描述符
            foreach(var reg in availableIntegerRegisters.Concat(availableFloatRegisters))
            {
                registerDescriptors[reg] = new RegisterDescriptor();
            }
        }

        /// <summary>
        /// 主要的寄存器分配方法
        /// </summary>
        public RegisterAllocationResult AllocateRegisters(List<BasicBlock> blocks, ILUnit ir)
        {
            // 1. 为所有变量创建描述符
            InitializeVariableDescriptors(blocks, ir);

            // 2. 构建干扰图
            BuildInterferenceGraph(blocks);

            // 3. 图着色
            bool success = ColorGraph();

            // 4. 如果着色失败，进行溢出处理
            if(!success)
            {
                HandleSpilling();
            }

            // 5. 返回分配结果
            return CreateAllocationResult();
        }

        /// <summary>
        /// 初始化变量描述符
        /// </summary>
        private void InitializeVariableDescriptors(List<BasicBlock> blocks, ILUnit ir)
        {
            var allVariables = new HashSet<SymbolTable.Record>();

            // 收集所有变量
            foreach(var block in blocks)
            {
                allVariables.UnionWith(block.defDict.Keys);
                allVariables.UnionWith(block.useDict.Keys);
            }

            // 为每个变量创建描述符
            foreach(var variable in allVariables)
            {
                if(!variableDescriptors.ContainsKey(variable))
                {
                    variableDescriptors[variable] = new VariableDescriptor();
                }
            }
        }

        /// <summary>
        /// 构建干扰图
        /// </summary>
        private void BuildInterferenceGraph(List<BasicBlock> blocks)
        {
            // 为每个基本块计算活跃变量信息
            ComputeLivenessAnalysis(blocks);

            // 根据活跃变量信息构建干扰图
            foreach(var block in blocks)
            {
                var liveVars = new HashSet<SymbolTable.Record>(block.setOut);

                // 从基本块末尾向前遍历
                for(int i = block.endIdx; i >= block.startIdx; i--)
                {
                    var defVars = GetDefVariablesAtLine(block, i);
                    var useVars = GetUseVariablesAtLine(block, i);

                    // 对于每个定义的变量，与所有活跃变量产生干扰
                    foreach(var defVar in defVars)
                    {
                        foreach(var liveVar in liveVars)
                        {
                            interferenceGraph.AddInterference(defVar, liveVar,
                                variableDescriptors[defVar], variableDescriptors[liveVar]);
                        }
                        liveVars.Remove(defVar);
                    }

                    // 添加使用的变量到活跃集合
                    foreach(var useVar in useVars)
                    {
                        liveVars.Add(useVar);
                    }
                }
            }
        }

        /// <summary>
        /// 图着色算法
        /// </summary>
        private bool ColorGraph()
        {
            var nodes = interferenceGraph.GetAllNodes().ToList();
            var stack = new Stack<InterferenceNode>();
            var spilledNodes = new List<InterferenceNode>();

            // 简化阶段：移除度数小于K的节点
            while(nodes.Any(n => !n.isSpilled))
            {
                var nodeToRemove = nodes.FirstOrDefault(n => !n.isSpilled &&
                    GetAvailableRegisters(n.variable).Count > n.Degree);

                if(nodeToRemove != null)
                {
                    // 简化：移除度数小于可用寄存器数的节点
                    stack.Push(nodeToRemove);
                    interferenceGraph.RemoveNode(nodeToRemove);
                    nodes.Remove(nodeToRemove);
                }
                else
                {
                    // 溢出：选择一个节点进行溢出
                    var spillNode = SelectSpillNode(nodes.Where(n => !n.isSpilled));
                    if(spillNode != null)
                    {
                        spillNode.isSpilled = true;
                        spilledNodes.Add(spillNode);
                        nodes.Remove(spillNode);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // 选择阶段：为栈中的节点分配颜色（寄存器）
            while(stack.Count > 0)
            {
                var node = stack.Pop();
                var availableRegs = GetAvailableColors(node);

                if(availableRegs.Count > 0)
                {
                    var selectedRegister = availableRegs.First();
                    AssignRegisterToVariable(node, selectedRegister);
                }
                else
                {
                    // 无法分配寄存器，需要溢出
                    spilledNodes.Add(node);
                }
            }

            // 处理溢出节点
            foreach(var spilledNode in spilledNodes)
            {
                AssignMemoryToVariable(spilledNode);
            }

            return spilledNodes.Count == 0;
        }

        /// <summary>
        /// 为变量分配寄存器 - 更新VariableDescriptor和RegisterDescriptor
        /// </summary>
        private void AssignRegisterToVariable(InterferenceNode node, RegisterEnum register)
        {
            var varDesc = node.descriptor;
            var regDesc = registerDescriptors[register];

            // 更新变量描述符：添加寄存器位置
            varDesc.positions.Add(new VarPos
            {
                posType = VarPos.PosType.Reg,
                addr = (long)register
            });

            // 更新寄存器描述符：记录该寄存器现在包含这个变量
            regDesc.variables.Add(varDesc);

            node.color = (int)register;
        }

        /// <summary>
        /// 为变量分配内存位置 - 溢出处理
        /// </summary>
        private void AssignMemoryToVariable(InterferenceNode node)
        {
            var varDesc = node.descriptor;

            // 计算内存偏移
            int variableSize = GetVariableSize(node.variable);
            long memoryOffset = currentSpillOffset;
            currentSpillOffset += variableSize;

            // 更新变量描述符：添加内存位置
            varDesc.positions.Add(new VarPos
            {
                posType = VarPos.PosType.Mem,
                addr = memoryOffset
            });

            node.isSpilled = true;
        }

        /// <summary>
        /// 获取变量可用的颜色（寄存器）
        /// </summary>
        private List<RegisterEnum> GetAvailableColors(InterferenceNode node)
        {
            var availableRegs = GetAvailableRegisters(node.variable);
            var usedRegs = new HashSet<RegisterEnum>();

            foreach(var neighbor in node.neighbors)
            {
                var assignedReg = neighbor.GetAssignedRegister();
                if(assignedReg.HasValue)
                {
                    usedRegs.Add(assignedReg.Value);
                }
            }

            return availableRegs.Where(reg => !usedRegs.Contains(reg)).ToList();
        }

        /// <summary>
        /// 获取变量可用的寄存器列表
        /// </summary>
        private List<RegisterEnum> GetAvailableRegisters(SymbolTable.Record variable)
        {
            if(IsFloatingPointVariable(variable))
            {
                return availableFloatRegisters;
            }
            else
            {
                return availableIntegerRegisters;
            }
        }

        /// <summary>
        /// 创建分配结果
        /// </summary>
        private RegisterAllocationResult CreateAllocationResult()
        {
            var result = new RegisterAllocationResult();

            foreach(var kvp in variableDescriptors)
            {
                var variable = kvp.Key;
                var descriptor = kvp.Value;

                // 检查变量是否分配到寄存器
                var regPos = descriptor.positions.FirstOrDefault(pos => pos.posType == VarPos.PosType.Reg);
                if(regPos.posType == VarPos.PosType.Reg)
                {
                    result.allocation[variable] = (RegisterEnum)regPos.addr;
                }
                else
                {
                    // 变量被溢出到内存
                    result.spilledVariables.Add(variable);
                    var memPos = descriptor.positions.FirstOrDefault(pos => pos.posType == VarPos.PosType.Mem);
                    if(memPos.posType == VarPos.PosType.Mem)
                    {
                        result.spillOffsets[variable] = (int)memPos.addr;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 活跃性分析
        /// </summary>
        private void ComputeLivenessAnalysis(List<BasicBlock> blocks)
        {
            bool changed = true;

            while(changed)
            {
                changed = false;

                foreach(var block in blocks)
                {
                    // 计算新的setOut
                    var newOut = new HashSet<SymbolTable.Record>();
                    foreach(var successor in block.successors)
                    {
                        newOut.UnionWith(successor.setIn);
                    }

                    // 计算新的setIn
                    var newIn = new HashSet<SymbolTable.Record>(newOut);

                    // setIn = use ∪ (setOut - def)
                    foreach(var defVar in GetDefVariables(block))
                    {
                        newIn.Remove(defVar);
                    }
                    foreach(var useVar in GetUseVariables(block))
                    {
                        newIn.Add(useVar);
                    }

                    // 检查是否有变化
                    if(!newIn.SetEquals(block.setIn) || !newOut.SetEquals(block.setOut))
                    {
                        changed = true;
                        block.setIn = newIn.ToList();
                        block.setOut = newOut.ToList();
                    }
                }
            }
        }

        // 辅助方法
        private List<SymbolTable.Record> GetDefVariablesAtLine(BasicBlock block, int line)
        {
            return block.defDict.Where(kvp => kvp.Value.Contains(line)).Select(kvp => kvp.Key).ToList();
        }

        private List<SymbolTable.Record> GetUseVariablesAtLine(BasicBlock block, int line)
        {
            return block.useDict.Where(kvp => kvp.Value.Contains(line)).Select(kvp => kvp.Key).ToList();
        }

        private List<SymbolTable.Record> GetDefVariables(BasicBlock block)
        {
            return block.defDict.Keys.ToList();
        }

        private List<SymbolTable.Record> GetUseVariables(BasicBlock block)
        {
            return block.useDict.Keys.ToList();
        }

        private InterferenceNode SelectSpillNode(IEnumerable<InterferenceNode> candidates)
        {
            if(!candidates.Any())
                return null;
            return candidates.OrderByDescending(n => n.Degree).First();
        }

        private void HandleSpilling()
        {
            // 溢出处理在ColorGraph方法中已经集成
        }

        private int GetVariableSize(SymbolTable.Record variable)
        {
            return variable.typeExpression switch
            {
                "int" => 4,
                "float" => 4,
                "double" => 8,
                "bool" => 1,
                "char" => 2,
                _ => 8 // 指针类型
            };
        }

        private bool IsFloatingPointVariable(SymbolTable.Record variable)
        {
            return variable.typeExpression == "float" || variable.typeExpression == "double";
        }

        /// <summary>
        /// 获取变量的当前位置信息
        /// </summary>
        public VariableDescriptor GetVariableDescriptor(SymbolTable.Record variable)
        {
            return variableDescriptors.ContainsKey(variable) ? variableDescriptors[variable] : null;
        }

        /// <summary>
        /// 获取寄存器的当前使用信息
        /// </summary>
        public RegisterDescriptor GetRegisterDescriptor(RegisterEnum register)
        {
            return registerDescriptors.ContainsKey(register) ? registerDescriptors[register] : null;
        }

        /// <summary>
        /// 释放寄存器
        /// </summary>
        public void FreeRegister(RegisterEnum register)
        {
            if(registerDescriptors.ContainsKey(register))
            {
                var regDesc = registerDescriptors[register];

                // 从所有相关变量描述符中移除该寄存器位置
                foreach(var varDesc in regDesc.variables)
                {
                    varDesc.positions.RemoveAll(pos => pos.posType == VarPos.PosType.Reg && pos.addr == (long)register);
                }

                // 清空寄存器描述符
                regDesc.variables.Clear();
            }
        }
    }

    /// <summary>
    /// 寄存器分配结果
    /// </summary>
    public class RegisterAllocationResult
    {
        public Dictionary<SymbolTable.Record, RegisterEnum> allocation = new Dictionary<SymbolTable.Record, RegisterEnum>();
        public List<SymbolTable.Record> spilledVariables = new List<SymbolTable.Record>();
        public Dictionary<SymbolTable.Record, int> spillOffsets = new Dictionary<SymbolTable.Record, int>();
    }
}