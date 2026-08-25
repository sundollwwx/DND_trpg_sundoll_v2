using System;
using System.Collections.Generic;

namespace Sundoll.Application
{
    /// <summary>
    /// Small deterministic pool used by presentation adapters. It owns reuse
    /// bookkeeping but never owns authoritative world state.
    /// </summary>
    public sealed class M7ReusablePool<T> where T : class
    {
        private readonly Func<T> factory;
        private readonly Action<T> reset;
        private readonly Stack<T> available = new Stack<T>();
        private readonly HashSet<T> leased = new HashSet<T>();

        public M7ReusablePool(Func<T> factory, Action<T> reset = null)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.reset = reset;
        }

        public int AvailableCount => available.Count;
        public int LeasedCount => leased.Count;

        public T Rent()
        {
            var item = available.Count == 0 ? factory() : available.Pop();
            if (item == null)
            {
                throw new InvalidOperationException("Pool factory returned null.");
            }

            leased.Add(item);
            return item;
        }

        public void Return(T item)
        {
            if (item == null || !leased.Remove(item))
            {
                return;
            }

            reset?.Invoke(item);
            available.Push(item);
        }

        public void Clear()
        {
            available.Clear();
            leased.Clear();
        }
    }
}
