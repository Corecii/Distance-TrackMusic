using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Corecii.TrackMusic
{
    public class Attached<T>
    {
        List<object> toRemove = new List<object>();
        Dictionary<object, T> attached = new Dictionary<object, T>();

        public IEnumerable<KeyValuePair<object, T>> Pairs { get => attached.AsEnumerable(); }

        public T this[object obj]
        {
            get { return Get(obj); }
            set { Set(obj, value); }
        }

        public delegate T Or();

        public T GetOr(object obj, Or or)
        {
            T data = default(T);
            if (attached.TryGetValue(obj, out data))
            {
                return data;
            }
            data = or();
            attached[obj] = data;
            return data;
        }

        public T Get(object obj)
        {
            T data = default(T);
            attached.TryGetValue(obj, out data);
            return data;
        }

        public void Set(object obj, T data)
        {
            if (obj == null)
            {
                return;
            }
            attached[obj] = data;
        }

        public void Remove(object obj)
        {
            attached.Remove(obj);
        }

        public void Update()
        {
            foreach (var key in attached.Keys)
            {
                if (key == null)
                {
                    toRemove.Add(key);
                }
            }
            foreach (var obj in toRemove)
            {
                attached.Remove(obj);
            }
            toRemove.Clear();
        }
    }
}
