namespace LancerMcp.Services;

public interface IFingerprintService
{
    FingerprintResult Compute(IEnumerable<string> tokens);
}
