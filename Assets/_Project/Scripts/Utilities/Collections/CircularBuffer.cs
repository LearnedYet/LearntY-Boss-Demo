using System;
using System.Collections;
using System.Collections.Generic;

namespace AdaptiveBossArena.Utilities.Collections
{
    /// <summary>
    /// 固定容量环形缓冲区，写满后自动覆盖最旧元素。
    /// </summary>
    /// <remarks>
    /// 这是Boss战斗记忆的存储基础。构造后不分配、不扩容，使内存和单次采样成本不随战斗时长增长。
    /// 索引<c>0</c>始终是最旧保留元素，索引<see cref="Count"/>减一是最新元素。
    /// </remarks>
    /// <typeparam name="T">元素类型，值类型直接存储，不产生额外分配。</typeparam>
    public sealed class CircularBuffer<T> : IReadOnlyList<T>
    {
        private readonly T[] _items;

        /// <summary>下次写入元素占用的索引。</summary>
        private int _head;

        private int _count;

        /// <summary>创建最多保留<paramref name="capacity"/>个元素的缓冲区。</summary>
        /// <param name="capacity">最大保留元素数，必须大于零。</param>
        /// <exception cref="ArgumentOutOfRangeException">容量不是正数时抛出。</exception>
        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), capacity, "Capacity must be greater than zero.");
            }

            _items = new T[capacity];
        }

        /// <summary>丢弃最旧元素之前能够保留的最大数量。</summary>
        public int Capacity => _items.Length;

        /// <summary>当前保留的元素数。</summary>
        public int Count => _count;

        /// <summary>数量达到<see cref="Capacity"/>时为true。</summary>
        public bool IsFull => _count == _items.Length;

        /// <summary>缓冲区没有元素时为true。</summary>
        public bool IsEmpty => _count == 0;

        /// <summary>获取<paramref name="index"/>位置的元素，零表示最旧保留元素。</summary>
        /// <param name="index">保留窗口中从零开始的索引。</param>
        /// <exception cref="ArgumentOutOfRangeException">索引超出保留窗口时抛出。</exception>
        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(index), index, $"Index must be in the range [0, {_count}).");
                }

                return _items[PhysicalIndex(index)];
            }
        }

        /// <summary>最旧的保留元素。</summary>
        /// <exception cref="InvalidOperationException">缓冲区为空时抛出。</exception>
        public T Oldest => _count > 0
            ? _items[PhysicalIndex(0)]
            : throw new InvalidOperationException("Buffer is empty.");

        /// <summary>最近添加的元素。</summary>
        /// <exception cref="InvalidOperationException">缓冲区为空时抛出。</exception>
        public T Newest => _count > 0
            ? _items[PhysicalIndex(_count - 1)]
            : throw new InvalidOperationException("Buffer is empty.");

        /// <summary>
        /// 添加元素，已满时丢弃最旧元素。
        /// </summary>
        /// <param name="item">要添加的元素，按引用传递以避免复制大结构体。</param>
        public void Add(in T item)
        {
            _items[_head] = item;
            _head = _head + 1 == _items.Length ? 0 : _head + 1;

            if (_count < _items.Length)
            {
                _count++;
            }
        }

        /// <summary>尝试读取最新元素，空缓冲区不会抛异常。</summary>
        /// <param name="item">接收最新元素，缓冲区为空时为<c>default</c>。</param>
        /// <returns>存在可读取元素时返回true。</returns>
        public bool TryGetNewest(out T item)
        {
            if (_count == 0)
            {
                item = default;
                return false;
            }

            item = _items[PhysicalIndex(_count - 1)];
            return true;
        }

        /// <summary>
        /// 清空所有保留元素并复用底层存储，不产生分配。
        /// </summary>
        public void Clear()
        {
            // 引用类型必须清除引用，避免缓冲区继续持有无用对象。
            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            _count = 0;
        }

        /// <summary>返回从旧到新遍历保留窗口的无分配枚举器。</summary>
        /// <returns>可在<c>foreach</c>中使用且无需装箱的值类型枚举器。</returns>
        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>将逻辑索引映射到底层数组索引，零表示最旧元素。</summary>
        private int PhysicalIndex(int logicalIndex)
        {
            // _head - _count不小于-Capacity，因此一次环绕修正即可。
            int start = _head - _count + _items.Length;
            int offset = start + logicalIndex;
            return offset % _items.Length;
        }

        /// <summary>从最旧到最新遍历保留窗口的值类型枚举器。</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly CircularBuffer<T> _buffer;
            private int _index;

            internal Enumerator(CircularBuffer<T> buffer)
            {
                _buffer = buffer;
                _index = -1;
            }

            /// <summary>当前游标位置的元素。</summary>
            public T Current => _buffer[_index];

            object IEnumerator.Current => Current;

            /// <summary>将游标向最新元素推进。</summary>
            /// <returns>还有元素时返回true。</returns>
            public bool MoveNext() => ++_index < _buffer.Count;

            /// <summary>将游标重置到最旧元素之前。</summary>
            public void Reset() => _index = -1;

            /// <summary>不持有非托管资源，仅为满足接口提供此方法。</summary>
            public void Dispose()
            {
            }
        }
    }
}
