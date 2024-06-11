namespace riri.globalredirector.Interfaces;

public interface IRedirectorApi
{
    public void AddTargetRaw(string name, int lengthBytes, string sigscan, Func<int, nuint> transformCb);
    public void AddTarget<TGlobalType>(string name, int lengthEntries, string sigscan, Func<int, nuint> transformCb) where TGlobalType : unmanaged;
    public unsafe nuint MoveGlobalRaw(nuint old, string name, int lengthBytes);
    public unsafe TGlobalType* MoveGlobal<TGlobalType>(TGlobalType* old, string name, int lengthEntries) where TGlobalType : unmanaged;
}