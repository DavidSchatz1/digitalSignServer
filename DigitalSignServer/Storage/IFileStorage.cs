using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace DigitalSignServer.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}