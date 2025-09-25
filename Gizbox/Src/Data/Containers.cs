using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace Gizbox
{
    [Serializable]
    [DataContract]
    public class GStack<T> : IEnumerable<T>
    {
        [DataMember]
        private List<T> data = new List<T>();

        public int Count => data.Count;

        public int Top => data.Count - 1;

        public T this[int idx]
        {
            get { return data[idx]; }
            set { data[idx] = value; }
        }

        public T Peek()
        {
            return data[Top];
        }

        public void Push(T ele)
        {
            data.Add(ele);
        }
        public T Pop()
        {
            var result = data[Top];
            data.RemoveAt(Top);
            return result;
        }

        public void Clear()
        {
            this.data.Clear();
        }

        public List<T> AsList()
        {
            return data;
        }

        //从栈顶到栈底
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            for(int i = Top; i >= 0; i--)
            {
                yield return data[i];
            }
        }

        public IEnumerator GetEnumerator()
        {
            for(int i = Top; i >= 0; i--)
            {
                yield return data[i];
            }
        }
    }

    public class BiDictionary<T1, T2> : IEnumerable<KeyValuePair<T1, T2>>
    {
        public Dictionary<T1, T2> dict = new();
        public Dictionary<T2, T1> dictInverse = new();

        public T2 this[T1 key]
        {
            get { return dict[key]; }
            set
            {
                dict[key] = value;
                dictInverse[value] = key;
            }
        }
        public T1 this[T2 key]
        {
            get { return dictInverse[key]; }
            set
            {
                dictInverse[key] = value;
                dict[value] = key;
            }
        }


        public bool ContainsKey(T1 key)
        {
            return dict.ContainsKey(key);
        }
        public bool ContainsKey(T2 key)
        {
            return dictInverse.ContainsKey(key);
        }

        public void Clear()
        {
            dict.Clear();
            dictInverse.Clear();
        }

        public IEnumerator<KeyValuePair<T1, T2>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return dict.GetEnumerator();
        }

        public void TryAdd(T1 x, T2 y)
        {
            if (!dict.ContainsKey(x) && !dictInverse.ContainsKey(y))
            {
                dict[x] = y;
                dictInverse[y] = x;
            }
        }
    }

    public class LList<T> : IEnumerable<T>
    {
        public class Node
        {
            public T value;
            public LList<T> owner;
            private Node next;
            private Node prev;
            public Node(T v, LList<T> powner, Node pprev = null, Node pnext = null)
            {
                this.value = v;
                this.next = pnext;
                this.prev = pprev;
                this.owner = powner;
            }
            public Node Prev
            {
                get => prev;
                set
                {
                    prev = value;
                    owner.Rebuild();
                }
            }
            public Node Next
            {
                get => next;
                set
                {
                    next = value;
                    owner.Rebuild();
                }
            }
            public void SetNextInternal(Node node)
            {
                next = node;
            }
            public void SetPrevInternal(Node node)
            {
                prev = node;
            }
        }

        public Node head;
        public Node tail;
        public HashSet<Node> allNodes = new();
        public int Count { get; private set; }
        public LList()
        {
            head = null;
            tail = null;
            Count = 0;
        }

        public Node Last => tail;
        public Node First => head;

        public void Rebuild()
        {
            if(head == null)
                Count = 0;

            allNodes.Clear();

            int newCount = 0;
            var curr = head;
            while(curr != null)
            {
                newCount++;
                allNodes.Add(curr);

                curr = curr.Next;
            }
            this.Count = newCount;
        }

        public Node AddLast(T value)
        {
            if (head == null)
            {
                Node firstNode = new Node(value, this, null, null);
                head = firstNode;
                tail = firstNode;
                allNodes.Add(firstNode);
                Count++;

                return firstNode;
            }

            Node newNode = new Node(value, this, tail, null);
            tail.SetNextInternal(newNode);
            tail = newNode;
            allNodes.Add(newNode);
            Count++;

            return newNode;
        }

        public Node AddFirst(T value)
        {
            if(head == null)
            {
                Node firstNode = new Node(value, this, null, null);
                head = firstNode;
                tail = firstNode;
                allNodes.Add(firstNode);
                Count++;

                return firstNode;
            }

            Node newNode = new Node(value, this, null, head);
            head.SetPrevInternal(newNode);
            head = newNode;
            allNodes.Add(newNode);
            Count++;

            return newNode;
        }

        public Node InsertBefore(Node targetNode, T val)
        {
            return targetNode.Prev != null 
                ? this.InsertAfter(targetNode.Prev, val) 
                : this.AddFirst(val);
        }

        public Node InsertAfter(Node targetNode, T val)
        {
            if(targetNode == null || targetNode.owner != this || allNodes.Contains(targetNode) == false)
                throw new Exception("invalid operation.");

            Node newNode = new Node(val, this, targetNode, targetNode.Next);

            if(targetNode.Next != null)
            {
                targetNode.Next.SetPrevInternal(newNode);
            }
            else
            {
                tail = newNode;
            }

            targetNode.SetNextInternal(newNode);

            allNodes.Add(newNode);
            Count++;

            return newNode;
        }

        public void MoveBefore(Node targetNode, Node nodeToMove)
        {
            if(allNodes.Contains(targetNode) == false)
                throw new Exception("invalid MoveBefore.");
            if(allNodes.Contains(nodeToMove) == false)
                throw new Exception("invalid MoveBefore.");


            //暂时移出node
            if(nodeToMove == head)
            {
                head = nodeToMove.Next;
            }
            if(nodeToMove == tail)
            {
                tail = nodeToMove.Prev;
            }
            if(nodeToMove.Prev != null)
            {
                nodeToMove.Prev.SetNextInternal(nodeToMove.Next);
            }
            if(nodeToMove.Next != null)
            {
                nodeToMove.Next.SetPrevInternal(nodeToMove.Prev);
            }
            nodeToMove.SetPrevInternal(null);
            nodeToMove.SetNextInternal(null);


            //插入node  
            if(targetNode == head)
            {
                head = nodeToMove;
                targetNode.SetPrevInternal(nodeToMove);
                nodeToMove.SetNextInternal(targetNode);
            }
            else
            {
                targetNode.Prev.SetNextInternal(nodeToMove);
                nodeToMove.SetPrevInternal(targetNode.Prev);

                targetNode.SetPrevInternal(nodeToMove);
                nodeToMove.SetNextInternal(targetNode);
            }
        }

        public void Remove(Node node)
        {
            if(node.owner != this)
                throw new Exception("invalid operation.");
            if(allNodes.Contains(node) == false)
                throw new Exception("invalid operation.");


            //不是头节点
            if(node.Prev != null)
            {
                node.Prev.SetNextInternal(node.Next);
            }
            //是头节点
            else
            {
                head = node.Next;
            }

            //不是尾节点
            if(node.Next != null)
            {
                node.Next.SetPrevInternal(node.Prev);
            }
            //是尾节点
            else
            {
                tail = node.Prev;
            }
            Count--;
            allNodes.Remove(node);
        }

        public void MoveRangeBefore(Node targetNode, Node fromNode, Node toNode)
        {
            if(allNodes.Contains(targetNode) == false)
                throw new Exception("invalid operation.");
            if(allNodes.Contains(fromNode) == false)
                throw new Exception("invalid operation.");
            if(allNodes.Contains(toNode) == false)
                throw new Exception("invalid operation.");

            int rangeLen = 0;
            var curr = fromNode;

            //验证from在to前面  
            while(curr != toNode)
            {
                rangeLen++;
                if(curr == targetNode)
                    throw new Exception("LList.MoveRangeBefore invalid operation.");
                if(curr.Next == targetNode)
                    throw new Exception("LList.MoveRangeBefore invalid operation.");

                if(rangeLen > this.Count)
                {
                    throw new Exception("LList.MoveRangeBefore invalid operation.");
                    break;
                }

                curr = curr.Next;
            }

            //区间不在头部
            if(fromNode.Prev != null)
            {
                if(targetNode == head)
                    head = fromNode;
                fromNode.Prev.SetNextInternal(toNode.Next);
            }
            //区间在头部
            else
            {
                head = (targetNode.Prev != null) ? targetNode.Prev : fromNode;
            }

            //区间不在尾部
            if(toNode.Next != null)
            {
                toNode.Next.SetPrevInternal(fromNode.Prev);
            }
            //区间在尾部
            else
            {
                tail = fromNode.Prev;
            }


            fromNode.SetPrevInternal(null);
            toNode.SetNextInternal(null);

            if(targetNode.Prev != null)
            {
                targetNode.Prev.SetNextInternal(fromNode);
                fromNode.SetPrevInternal(targetNode.Prev);
                head = fromNode;
            }

            targetNode.SetPrevInternal(toNode);
            toNode.SetNextInternal(targetNode);

            Rebuild();
        }

        public IEnumerator<T> GetEnumerator()
        {
            if(head == null)
                yield break;

            var curr = head;
            while(curr != null)
            {
                yield return curr.value;
                curr = curr.Next;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if(head == null)
                yield break;

            var curr = head;
            while(curr != null)
            {
                yield return curr.value;
                curr = curr.Next;
            }
        }
    }


    public class GTree<T>
    {
        public class Node
        {
            public T value;
            public List<Node> children = new();
            public Node parent;
            public int depth = 0;

            public Node Add(T val)
            {
                Node c = new();
                c.value = val;

                Add(c);

                return c;
            }
            public void Add(Node c)
            {
                c.parent = this;
                this.children.Add(c);
                c.depth = this.depth + 1;
            }

            public void Remove(Node n)
            {
                if(children.Contains(n) == false)
                    return;

                children.Remove(n);
                n.parent = null;
                n.depth = 0;
            }

            public IEnumerable<Node> TraverseNode()
            {
                yield return this;
                foreach(var child in children)
                {
                    foreach(var v in child.TraverseNode())
                    {
                        yield return v;
                    }
                }
            }
            public IEnumerable<T> TraverseDepthFirst()
            {
                foreach(var child in children)
                {
                    foreach(var v in child.TraverseDepthFirst())
                    {
                        yield return v;
                    }
                }
                yield return value;
            }
        }

        public Node root = new();


        public IEnumerable<T> TraverseDepthFirst()
        {
            if(root != null)
                return root.TraverseDepthFirst();

            throw new GizboxException(ExceptioName.Undefine, "root node null.");
        }

        public IEnumerable<Node> TraverseNode()
        {
            if(root != null)
                return root.TraverseNode();

            throw new GizboxException(ExceptioName.Undefine, "root node null.");
        }

        public void Print()
        {
            System.Text.StringBuilder strb = new System.Text.StringBuilder();

            foreach(var node in TraverseNode())
            {
                string brace = "";
                string lineChar = "┖   ";
                for(int i = 0; i < node.depth; ++i)
                {
                    brace += "    ";
                }
                strb.AppendLine(brace + lineChar + node.value);
            }

            Gizbox.GixConsole.WriteLine(strb.ToString());
        }
    }
}




    // 可插入有序的链表 + 区间索引
    // 目标：插入不影响已存在区间边界；按节点快速查询命中区间
    public sealed class LinkedIntervalList<T>
    {
        public sealed class Node
        {
            internal Node Prev;
            internal Node Next;
            internal long Label; // 用于全序与区间索引
            public T Value;

            internal Node(T value, long label)
            {
                Value = value;
                Label = label;
            }

            public Node Previous => Prev;
            public Node NextNode => Next;
        }

        public sealed class Interval
        {
            internal long Id;
            internal Node StartNode;
            internal Node EndNode;
            internal long StartLabel;
            internal long EndLabel;

            // 便于删除：记录其所在的 IntervalTree 节点
            public IntervalTree.Node OwnerTreeNode;

            public Node Start => StartNode;
            public Node End => EndNode;

            public override string ToString() => $"[{StartLabel}, {EndLabel}]";
        }

        private Node _head;
        private Node _tail;
        private int _count;
        private long _nextIntervalId = 1;

        // 初始步长，便于在中间插入时取 Label 中点
        private const long InitialStep = 1_000;

        private readonly IntervalTree _index = new IntervalTree();

        public int Count => _count;
        public Node Head => _head;
        public Node Tail => _tail;

        public Node AddFirst(T value)
        {
            if(_head == null)
            {
                var node = new Node(value, 0);
                _head = _tail = node;
                _count = 1;
                return node;
            }
            return InsertBefore(_head, value);
        }

        public Node AddLast(T value)
        {
            if(_tail == null)
            {
                var node = new Node(value, 0);
                _head = _tail = node;
                _count = 1;
                return node;
            }
            return InsertAfter(_tail, value);
        }

        public Node InsertAfter(Node node, T value)
        {
            if(node == null)
                throw new ArgumentNullException(nameof(node));
            var next = node.Next;
            long label;
            if(next == null)
            {
                label = node.Label + InitialStep;
            }
            else
            {
                label = Mid(node.Label, next.Label);
                if(label == node.Label || label == next.Label)
                {
                    RelabelAll();
                    // 重新计算
                    label = Mid(node.Label, next.Label);
                }
            }

            var newNode = new Node(value, label);
            // 链接
            newNode.Prev = node;
            newNode.Next = next;
            node.Next = newNode;
            if(next != null)
                next.Prev = newNode;
            if(_tail == node)
                _tail = newNode;
            _count++;
            return newNode;
        }

        public Node InsertBefore(Node node, T value)
        {
            if(node == null)
                throw new ArgumentNullException(nameof(node));
            var prev = node.Previous;
            long label;
            if(prev == null)
            {
                label = node.Label - InitialStep;
            }
            else
            {
                label = Mid(prev.Label, node.Label);
                if(label == prev.Label || label == node.Label)
                {
                    RelabelAll();
                    label = Mid(prev.Label, node.Label);
                }
            }

            var newNode = new Node(value, label);
            // 链接
            newNode.Next = node;
            newNode.Prev = prev;
            node.Prev = newNode;
            if(prev != null)
                prev.Next = newNode;
            if(_head == node)
                _head = newNode;
            _count++;
            return newNode;
        }

        // 注意：若删除作为某些区间的端点的节点，需先调整/删除这些区间再删节点
        public void Remove(Node node)
        {
            if(node == null)
                throw new ArgumentNullException(nameof(node));
            // 保护：不允许直接删除仍被作为区间端点的节点
            if(_index.HasIntervalEndpoint(node.Label))
                throw new InvalidOperationException("先移除或调整以该节点作为端点的区间，再删除该节点。");

            var prev = node.Previous;
            var next = node.NextNode;

            if(prev != null)
                prev.Next = next;
            else
                _head = next;
            if(next != null)
                next.Prev = prev;
            else
                _tail = prev;
            _count--;
        }

        // 定义一个区间，端点顺序可任意；内部会规范化 [minLabel, maxLabel]
        public Interval AddInterval(Node a, Node b)
        {
            if(a == null || b == null)
                throw new ArgumentNullException("区间端点不可为空。");
            long s = a.Label;
            long e = b.Label;
            if(s > e)
            { var t = s; s = e; e = t; }

            var iv = new Interval
            {
                Id = _nextIntervalId++,
                StartNode = a,
                EndNode = b,
                StartLabel = s,
                EndLabel = e
            };
            _index.Insert(iv);
            return iv;
        }

        public void RemoveInterval(Interval iv)
        {
            if(iv == null)
                return;
            _index.Remove(iv);
        }

        // 查询：给定节点，返回包含该节点的所有区间
        public List<Interval> GetIntervalsAt(Node node)
        {
            if(node == null)
                throw new ArgumentNullException(nameof(node));
            var result = new List<Interval>();
            _index.Stab(node.Label, result);
            return result;
        }

        // 全表重标：线性一次，极少发生（只有在无间隙时）
        private void RelabelAll()
        {
            long label = 0;
            var cur = _head;
            while(cur != null)
            {
                cur.Label = label;
                label += InitialStep;
                cur = cur.Next;
            }

            // 重标不影响已建区间（区间以端点“节点引用”为准），
            // 但索引中存储的是标签范围，需要重新构建索引
            _index.RebuildFromIntervals(GetAllIntervalsSnapshot());
        }

        private static long Mid(long a, long b)
        {
            // 防溢出的中点
            return a + (b - a) / 2;
        }

        // 维护 Interval 列表的快照以便重建（仅在极端情况下使用）
        private List<Interval> GetAllIntervalsSnapshot()
        {
            return _index.GetAllIntervals();
        }

        // ---------------- Interval Tree（中心分割法，动态插入/删除，刺探查询） ----------------

        public sealed class IntervalTree
        {
            public sealed class Node
            {
                internal long Center;
                internal Node Left;
                internal Node Right;
                // 覆盖 Center 的区间：
                // 为提升查询效率，同时维护两种排序
                internal readonly List<Interval> ByStartAsc = new List<Interval>();
                internal readonly List<Interval> ByEndDesc = new List<Interval>();

                internal Node(long center) { Center = center; }
            }

            private Node _root;
            private readonly HashSet<long> _endpointLabels = new HashSet<long>(); // 用于防止删除端点节点（可选）
            private readonly List<Interval> _all = new List<Interval>(); // 重建用

            // 动态插入
            internal void Insert(Interval iv)
            {
                _all.Add(iv);
                _endpointLabels.Add(iv.StartLabel);
                _endpointLabels.Add(iv.EndLabel);

                if(_root == null)
                {
                    _root = new Node(Mid(iv.StartLabel, iv.EndLabel));
                    AddToNode(_root, iv);
                    return;
                }
                Insert(_root, iv);
            }

            internal void Remove(Interval iv)
            {
                if(iv.OwnerTreeNode != null)
                {
                    RemoveFromNode(iv.OwnerTreeNode, iv);
                }
                _all.Remove(iv);
                // 端点是否仍被其它区间使用？简单起见，保留/或尝试移除（非功能关键）
                // 这里不移除，HasIntervalEndpoint 仅用作保护，并不要求绝对精准
            }

            internal bool HasIntervalEndpoint(long label) => _endpointLabels.Contains(label);

            internal void Stab(long x, List<Interval> result)
            {
                Stab(_root, x, result);
            }

            internal List<Interval> GetAllIntervals()
            {
                return new List<Interval>(_all);
            }

            internal void RebuildFromIntervals(List<Interval> intervals)
            {
                _root = null;
                _endpointLabels.Clear();
                _all.Clear();
                foreach(var iv in intervals)
                {
                    // 端点的标签改变了（重标），需要同步刷新
                    iv.StartLabel = Math.Min(iv.StartNode.Label, iv.EndNode.Label);
                    iv.EndLabel = Math.Max(iv.StartNode.Label, iv.EndNode.Label);
                    Insert(iv);
                }
            }

            private void Insert(Node node, Interval iv)
            {
                if(iv.EndLabel < node.Center)
                {
                    if(node.Left == null)
                    {
                        node.Left = new Node(Mid(iv.StartLabel, iv.EndLabel));
                        AddToNode(node.Left, iv);
                    }
                    else
                        Insert(node.Left, iv);
                }
                else if(iv.StartLabel > node.Center)
                {
                    if(node.Right == null)
                    {
                        node.Right = new Node(Mid(iv.StartLabel, iv.EndLabel));
                        AddToNode(node.Right, iv);
                    }
                    else
                        Insert(node.Right, iv);
                }
                else
                {
                    AddToNode(node, iv);
                }
            }

            private static void AddToNode(Node node, Interval iv)
            {
                // 插入到覆盖Center的列表中
                // ByStartAsc按StartLabel升序
                int i = node.ByStartAsc.BinarySearch(iv, StartAscComparer.Instance);
                if(i < 0)
                    i = ~i;
                node.ByStartAsc.Insert(i, iv);

                // ByEndDesc按EndLabel降序
                int j = node.ByEndDesc.BinarySearch(iv, EndDescComparer.Instance);
                if(j < 0)
                    j = ~j;
                node.ByEndDesc.Insert(j, iv);

                iv.OwnerTreeNode = node;
            }

            private static void RemoveFromNode(Node node, Interval iv)
            {
                // 线性移除也可，但这里利用二分近似定位后向两边扫描
                int i = node.ByStartAsc.BinarySearch(iv, StartAscComparer.Instance);
                if(i < 0)
                    i = ~i;
                // 扫描定位相同元素
                for(int k = Math.Max(0, i - 2); k < node.ByStartAsc.Count && k <= i + 2; k++)
                {
                    if(ReferenceEquals(node.ByStartAsc[k], iv))
                    {
                        node.ByStartAsc.RemoveAt(k);
                        break;
                    }
                }
                int j = node.ByEndDesc.BinarySearch(iv, EndDescComparer.Instance);
                if(j < 0)
                    j = ~j;
                for(int k = Math.Max(0, j - 2); k < node.ByEndDesc.Count && k <= j + 2; k++)
                {
                    if(ReferenceEquals(node.ByEndDesc[k], iv))
                    {
                        node.ByEndDesc.RemoveAt(k);
                        break;
                    }
                }
                iv.OwnerTreeNode = null;
            }

            private static void Stab(Node node, long x, List<Interval> result)
            {
                if(node == null)
                    return;

                if(x < node.Center)
                {
                    // 按Start升序扫描，直到Start > x 即可停止
                    var list = node.ByStartAsc;
                    for(int i = 0; i < list.Count; i++)
                    {
                        var iv = list[i];
                        if(iv.StartLabel <= x)
                            result.Add(iv);
                        else
                            break;
                    }
                    Stab(node.Left, x, result);
                }
                else if(x > node.Center)
                {
                    // 按End降序扫描，直到End < x 即可停止
                    var list = node.ByEndDesc;
                    for(int i = 0; i < list.Count; i++)
                    {
                        var iv = list[i];
                        if(iv.EndLabel >= x)
                            result.Add(iv);
                        else
                            break;
                    }
                    Stab(node.Right, x, result);
                }
                else
                {
                    // x == Center，当前节点列表全命中
                    result.AddRange(node.ByStartAsc);
                }
            }

            private static long Mid(long a, long b) => a + (b - a) / 2;

            private sealed class StartAscComparer : IComparer<Interval>
            {
                public static readonly StartAscComparer Instance = new StartAscComparer();
                public int Compare(Interval x, Interval y)
                {
                    int c = x.StartLabel.CompareTo(y.StartLabel);
                    if(c != 0)
                        return c;
                    c = x.EndLabel.CompareTo(y.EndLabel);
                    if(c != 0)
                        return c;
                    return x.Id.CompareTo(y.Id);
                }
            }

            private sealed class EndDescComparer : IComparer<Interval>
            {
                public static readonly EndDescComparer Instance = new EndDescComparer();
                public int Compare(Interval x, Interval y)
                {
                    int c = y.EndLabel.CompareTo(x.EndLabel); // 降序
                    if(c != 0)
                        return c;
                    c = x.StartLabel.CompareTo(y.StartLabel);
                    if(c != 0)
                        return c;
                    return x.Id.CompareTo(y.Id);
                }
            }
        }
    }