using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;



namespace Gizbox.ScriptEngineV2
{
    public unsafe class SimlateX64
    {
        //参数的寄存器分配示例：  
        //func3(int a, double b, int c, float d, int e, float f);
        // a in RCX, b in XMM1, c in R8, d in XMM3, f then e pushed on stack

        //func4(__m64 a, __m128 b, struct c, float d, __m128 e, __m128 f);
        // a in RCX, ptr to b in RDX, ptr to c in R8, d in XMM3,
        // ptr to f pushed on stack, then ptr to e pushed on stack


        //registers  
        private readonly byte[] RAX;//return value

        private readonly byte[] RCX;//integer or ptr arg 0
        private readonly byte[] RDX;//integer or ptr arg 1
        private readonly byte[] R8;//integer or ptr arg 2
        private readonly byte[] R9;//integer or ptr arg 3

        private readonly byte[] RBP;//frame pointer
        private readonly byte[] RSP;//stack pointer


        private readonly byte[] XMM0;//float arg 0
        private readonly byte[] XMM1;//float arg 1
        private readonly byte[] XMM2;//float arg 2
        private readonly byte[] XMM3;//float arg 3



        //handles  
        public List<GCHandle> handles = new List<GCHandle>();


        public SimlateX64()
        {
            RAX = new byte[8];
            handles.Add(GCHandle.Alloc(RAX, GCHandleType.Pinned));


            RCX = new byte[8];
            handles.Add(GCHandle.Alloc(RCX, GCHandleType.Pinned));
            RDX = new byte[8];
            handles.Add(GCHandle.Alloc(RDX, GCHandleType.Pinned));
            R8 = new byte[8];
            handles.Add(GCHandle.Alloc(R8, GCHandleType.Pinned));
            R9 = new byte[8];
            handles.Add(GCHandle.Alloc(R9, GCHandleType.Pinned));


            XMM0 = new byte[16];
            handles.Add(GCHandle.Alloc(XMM0, GCHandleType.Pinned));
            XMM1 = new byte[16];
            handles.Add(GCHandle.Alloc(XMM1, GCHandleType.Pinned));
            XMM2 = new byte[16];
            handles.Add(GCHandle.Alloc(XMM2, GCHandleType.Pinned));
            XMM3 = new byte[16];
            handles.Add(GCHandle.Alloc(XMM3, GCHandleType.Pinned));

        }
    }

    public unsafe class SimulateMemory : IDisposable
    {
        //unmanaged约束是不包含任何引用类型的值类型，比struct约束更严格  
        public unsafe class StackMem : IDisposable
        {
            private readonly byte[] _memory;
            private readonly GCHandle _handle;
            private readonly byte* _base_ptr;
            private readonly byte* _bottom_ptr;

            private readonly long _totalSize;
            private byte* _top;

            public StackMem(int sizeMB)
            {
                _totalSize = (long)(1024 * 1024 * sizeMB);
                _memory = new byte[_totalSize];
                _handle = GCHandle.Alloc(_memory, GCHandleType.Pinned);
                _base_ptr = (byte*)_handle.AddrOfPinnedObject().ToPointer();
                _bottom_ptr = _base_ptr + _totalSize - 1;
                _top = _bottom_ptr;
            }
            public void Dispose()
            {
                _handle.Free();
            }

            public byte* stack_malloc(int length)
            {
                _top -= length;
                return _top;
            }
        }
        public unsafe class HeapMem : IDisposable
        {
            private byte[] _memory;
            private readonly GCHandle _handle;
            private readonly byte* _base_ptr;
            private long _totalSize;
            private long _usedSize;
            private List<(long start, long size)> _allocatedBlocks;
            private List<(long start, long size)> _freeBlocks;

            public HeapMem(long totalSizeMB)
            {
                long totalSize = totalSizeMB * 1024 * 1024;

                _totalSize = totalSize;

                _memory = new byte[totalSize];
                _handle = GCHandle.Alloc(_memory, GCHandleType.Pinned);
                _base_ptr = (byte*)_handle.AddrOfPinnedObject().ToPointer();

                _usedSize = 0;
                _allocatedBlocks = new List<(long, long)>();
                _freeBlocks = new List<(long, long)> { (0, totalSize) };
            }
            public void Dispose()
            {
                _handle.Free();
            }

            public byte* malloc(long size)
            {
                for(int i = 0; i < _freeBlocks.Count; i++)
                {
                    var block = _freeBlocks[i];
                    if(block.size >= size)
                    {
                        // alloc
                        _allocatedBlocks.Add((block.start, size));
                        _usedSize += size;

                        if(block.size > size)
                        {
                            _freeBlocks[i] = (block.start + size, block.size - size);
                        }
                        else
                        {
                            _freeBlocks.RemoveAt(i);
                        }

                        return _base_ptr + block.start;
                    }
                }

                throw new OutOfMemoryException("Not enough memory to allocate.");
            }


            public void free(byte* ptr)
            {
                for(int i = 0; i < _allocatedBlocks.Count; i++)
                {
                    var block = _allocatedBlocks[i];
                    if((_base_ptr + block.start) == ptr)
                    {
                        // free
                        _allocatedBlocks.RemoveAt(i);
                        _usedSize -= block.size;

                        _freeBlocks.Add((block.start, block.size));

                        merge();
                        return;
                    }
                }

                throw new ArgumentException("Invalid memory address.");
            }

            private void merge()
            {
                _freeBlocks.Sort((a, b) => a.start.CompareTo(b.start));

                for(int i = 0; i < _freeBlocks.Count - 1; i++)
                {
                    var current = _freeBlocks[i];
                    var next = _freeBlocks[i + 1];

                    if(current.start + current.size == next.start)
                    {
                        // merge  
                        _freeBlocks[i] = (current.start, current.size + next.size);
                        _freeBlocks.RemoveAt(i + 1);
                        i--;
                    }
                }
            }
        }

        private StackMem stack;
        private HeapMem heap;

        private long heap_size;
        private long stack_size;
        private long stack_bottom;

        public SimulateMemory(int heapSizeMB, int stackSizeMB)
        {
            heap = new HeapMem(heapSizeMB);
            stack = new StackMem(stackSizeMB);
            heap_size = (heapSizeMB * 1024 * 1024);
            stack_size = (stackSizeMB * 1024 * 1024);
            stack_bottom = heap_size + stack_size;
        }
        public void Dispose()
        {
            stack.Dispose();
            heap.Dispose();
        }


        public byte* stack_malloc(int length)
        {
            return stack.stack_malloc(length);
        }

        public T* new_<T>() where T : unmanaged
        {
            return (T*)heap_malloc(sizeof(T));
        }

        public byte* heap_malloc(int length)
        {
            return heap.malloc(length);
        }
        public void heap_free(byte* ptr)
        {
            heap.free(ptr);
        }


        public void write<T>(byte* ptr, T data) where T : unmanaged
        {
            int size = sizeof(T);

            //todo: out of range exception...

            *(T*)ptr = data;
        }
        public T read<T>(byte* ptr) where T : unmanaged
        {
            int size = sizeof(T);

            //todo: out of range exception...

            return *(T*)ptr;
        }
    }


    public class SimMemUtility
    {
        public static void MemLayoutTest(long sp, (int, int)[] argArr, out long[] allocAddrs, out long spmovement)
        {
            allocAddrs = new long[argArr.Length];
            long ptr_sp = sp;

            // 强制初始栈指针16字节对齐（模拟x86-64 System V ABI）
            ptr_sp = AlignDown(ptr_sp, 16);

            // 按从右到左顺序压栈参数（C调用约定）
            for(int i = argArr.Length - 1; i >= 0; i--)
            {
                int size = argArr[i].Item1;
                int alignment = argArr[i].Item2;

                // 计算新的栈指针（考虑对齐）
                ptr_sp = AlignDown(ptr_sp - size, alignment);
                allocAddrs[i] = ptr_sp; // 记录当前参数的起始地址
            }

            // 最终栈指针必须保持16字节对齐（System V ABI要求）
            ptr_sp = AlignDown(ptr_sp, 16);
            spmovement = ptr_sp - sp;
        }

        // 内存对齐计算函数
        public static long AlignDown(long value, int alignment)
        {
            if(alignment <= 0 || (alignment & (alignment - 1)) != 0)
                throw new ArgumentException("Alignment must be power of two");
            return value & ~(alignment - 1);
        }

        // 类型大小计算
        public static int GetTypeSize(Type type)
        {
            if(type.IsPrimitive)
            {
                return System.Runtime.InteropServices.Marshal.SizeOf(type);
            }

            if(type.IsValueType)
            {
                int size = 0;
                int maxAlignment = 1;

                foreach(var field in type.GetFields())
                {
                    int fieldSize = GetTypeSize(field.FieldType);
                    int fieldAlignment = GetTypeAlignment(field.FieldType);

                    // 添加填充以满足字段对齐
                    int padding = (fieldAlignment - (size % fieldAlignment)) % fieldAlignment;
                    size += padding + fieldSize;

                    maxAlignment = Math.Max(maxAlignment, fieldAlignment);
                }

                // 结构体尾部填充
                int tailPadding = (maxAlignment - (size % maxAlignment)) % maxAlignment;
                return size + tailPadding;
            }

            throw new NotSupportedException($"Unsupported type: {type}");
        }

        // 类型对齐计算
        public static int GetTypeAlignment(Type type)
        {
            if(type.IsPrimitive)
            {
                return type switch
                {
                    _ when type == typeof(byte) => 1,
                    _ when type == typeof(short) => 2,
                    _ when type == typeof(int) => 4,
                    _ when type == typeof(long) => 8,
                    _ when type == typeof(float) => 4,
                    _ when type == typeof(double) => 8,
                    _ => throw new NotSupportedException()
                };
            }

            if(type.IsValueType)
            {
                int maxAlignment = 1;
                foreach(var field in type.GetFields())
                {
                    maxAlignment = Math.Max(maxAlignment, GetTypeAlignment(field.FieldType));
                }
                return maxAlignment;
            }

            throw new NotSupportedException($"Unsupported type: {type}");
        }

    }

}
