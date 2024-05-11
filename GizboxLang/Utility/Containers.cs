using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox
{
    public class Stack<T>
    {
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

        public List<T> ToList()
        {
            return data;
        }
    }
}
