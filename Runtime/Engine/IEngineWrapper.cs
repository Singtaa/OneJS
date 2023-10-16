namespace OneJS.Engine {
    public interface IEngineWrapper {
        void Init();
        void Reset();
        void Dispose();
        object GetValue(string name);
        void SetValue(string name, object? obj);
        void Call(object callback, object thisObj = null, params object[] arguments);
        void Execute(string code);
        object Evaluate(string code);
        void RunModule(string scriptPath);
    }
}
