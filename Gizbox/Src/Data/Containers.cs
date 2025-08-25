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

        public void AddLast(T value)
        {
            if (head == null)
            {
                Node firstNode = new Node(value, this, null, null);
                head = firstNode;
                tail = firstNode;
                allNodes.Add(firstNode);
                Count++;
                return;
            }

            Node newNode = new Node(value, this, tail, null);
            tail.SetNextInternal(newNode);
            tail = newNode;
            allNodes.Add(newNode);
            Count++;
        }

        public void AddFirst(T value)
        {
            if(head == null)
            {
                Node firstNode = new Node(value, this, null, null);
                head = firstNode;
                tail = firstNode;
                allNodes.Add(firstNode);
                Count++;
                return;
            }

            Node newNode = new Node(value, this, null, head);
            head.SetPrevInternal(newNode);
            head = newNode;
            allNodes.Add(newNode);
            Count++;
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
}