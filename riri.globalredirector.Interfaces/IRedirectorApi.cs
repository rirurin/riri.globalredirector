namespace riri.globalredirector.Interfaces;

public interface IRedirectorApi
{
    public void AddTarget(string name, int length, string sigscan, Func<int, nuint> transformCb);
    public unsafe TGlobalType* ReallocateGlobal<TGlobalType>(TGlobalType* old, string name, int newLength) where TGlobalType : unmanaged;
}