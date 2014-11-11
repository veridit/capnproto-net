﻿
using System;
using System.Collections;
using System.Collections.Generic;
namespace CapnProto.Take2
{
    public struct FixedSizeList<T> : IList<T>, IList
    {
        public override string ToString()
        {
            return pointer.ToString();
        }
        private readonly Pointer pointer;
        private FixedSizeList(Pointer pointer) { this.pointer = pointer; }
        public static explicit operator FixedSizeList<T>(Pointer pointer) { return new FixedSizeList<T>(pointer); }
        public static implicit operator Pointer(FixedSizeList<T> obj) { return obj.pointer; }
        public static bool operator true(FixedSizeList<T> obj) { return obj.pointer.IsValid; }
        public static bool operator false(FixedSizeList<T> obj) { return !obj.pointer.IsValid; }
        public static bool operator !(FixedSizeList<T> obj) { return !obj.pointer.IsValid; }

        public T this[int index]
        {
            get { return StructAccessor<T>.Instance.Get(pointer, index); }
            set { StructAccessor<T>.Instance.Set(pointer, index, value); }
        }
        object IList.this[int index]
        {
            get { return this[index]; }
            set { this[index] = (T)value; }
        }

        void IList<T>.Insert(int index, T value) { throw new NotSupportedException(); }
        void IList.Insert(int index, object value) { throw new NotSupportedException(); }
        void IList<T>.RemoveAt(int index) { throw new NotSupportedException(); }
        void IList.Remove(object value) { throw new NotSupportedException(); }
        void IList.RemoveAt(int index) { throw new NotSupportedException(); }
        public int Count() { return pointer.Count(); }

        void ICollection<T>.Add(T item) { throw new NotSupportedException(); }
        int IList.Add(object value) { throw new NotSupportedException(); }
        void ICollection<T>.Clear() { throw new NotSupportedException(); }
        void IList.Clear() { throw new NotSupportedException(); }

        public bool Contains(T value)
        {

            var pointer = this.pointer.Dereference();
            int count = pointer.Count();
            if (count != 0)
            {
                var comparer = EqualityComparer<T>.Default;
                var accessor = StructAccessor<T>.Instance;
                for (int i = 0; i < count; i++)
                {
                    if (comparer.Equals(accessor.Get(pointer, i), value)) return true;
                }
            }
            return false;
        }
        bool IList.Contains(object value)
        {
            return Contains((T)value);
        }
        public int IndexOf(T value)
        {
            var pointer = this.pointer.Dereference();
            int count = pointer.Count();
            if (count != 0)
            {
                var comparer = EqualityComparer<T>.Default;
                var accessor = StructAccessor<T>.Instance;
                for (int i = 0; i < count; i++)
                {
                    if (comparer.Equals(accessor.Get(pointer, i), value)) return i;
                }
            }
            return -1;
        }
        int IList.IndexOf(object value)
        {
            return IndexOf((T)value);
        }

        void ICollection<T>.CopyTo(T[] array, int arrayIndex)
        {
            var pointer = this.pointer.Dereference();
            int count = pointer.Count();
            if (count != 0)
            {
                var accessor = StructAccessor<T>.Instance;
                for (int i = 0; i < count; i++)
                {
                    array[arrayIndex++] = accessor.Get(pointer, i);
                }
            }
        }
        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            var pointer = this.pointer.Dereference();
            int count = pointer.Count();
            if (count != 0)
            {
                var accessor = StructAccessor<T>.Instance;
                for (int i = 0; i < count; i++)
                {
                    array.SetValue(accessor.Get(pointer, i), arrayIndex++);
                }
            }
        }

        int ICollection<T>.Count { get { return Count(); } }
        int ICollection.Count { get { return Count(); } }

        bool ICollection.IsSynchronized { get { return false; } }
        object ICollection.SyncRoot { get { return null; } }

        bool ICollection<T>.IsReadOnly { get { return false; } }
        bool IList.IsReadOnly { get { return false; } }
        bool IList.IsFixedSize { get { return true; } }

        bool ICollection<T>.Remove(T item) { throw new NotSupportedException(); }

        public IEnumerator<T> GetEnumerator()
        {
            var pointer = this.pointer.Dereference();
            int count = pointer.Count();
            if (count != 0)
            {
                var accessor = StructAccessor<T>.Instance;
                for (int i = 0; i < count; i++)
                    yield return accessor.Get(pointer, i);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public static FixedSizeList<T> Create(Pointer pointer, int count)
        {
            return StructAccessor<T>.Instance.CreateList(pointer, count);
        }
    }
}
