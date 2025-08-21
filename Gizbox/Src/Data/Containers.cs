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
}