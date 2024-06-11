namespace riri.globalredirector
{
    public interface Allocator
    {
        // Allocate an area assigned to "name" of area "lengthBytes". This fails if the allocation is expected
        // to exceed the maximum allowable address (baseAddress + 0xFFFFFFFF)
        public unsafe nuint Allocate(int lengthBytes, string name);
    }
}
