public interface IEnvironmentDestructible
{
    int CellX { get; }
    int CellZ { get; }
    bool IsDestroyed { get; }
    void SetCell(int x, int z);
    void ApplyDestroyed(bool burning);
}
