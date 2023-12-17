namespace OclUdp.Internals
{
    internal class ArrayFragment<T>
    {
        public T[] Array { get; set; }

        public int Offset { get; set; }

        public int Count { get; set; }

        public ArrayFragment(T[] array, int offset, int count) 
        {
            Array = array;
            Offset = offset;
            Count = count;
        }
    }
}
