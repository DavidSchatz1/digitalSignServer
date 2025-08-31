//using Amazon;
//using Amazon.S3;
//using Amazon.S3.Model;
//using DigitalSignServer.Options;
//using Microsoft.Extensions.Options;

//namespace DigitalSignServer.Storage;

//public sealed class S3FileStorage : IFileStorage
//{
//    private readonly IAmazonS3 _s3;
//    private readonly string _bucket;
//    private readonly string _basePrefix;

//    public S3FileStorage(IOptions<S3Options> opts)
//    {
//        var o = opts.Value;
//        if (string.IsNullOrWhiteSpace(o.Bucket)) throw new InvalidOperationException("S3:Bucket is required");
//        _bucket = o.Bucket!;
//        _basePrefix = (o.BasePrefix ?? string.Empty).Trim('/');


//        // Create the client using ambient creds (Instance Role / SSO / local profile)
//        _s3 = string.IsNullOrWhiteSpace(o.Region)
//        ? new AmazonS3Client()
//        : new AmazonS3Client(RegionEndpoint.GetBySystemName(o.Region));
//    }


//    private string BuildKey(string key)
//    {
//        var clean = key.Replace('\\', '/').TrimStart('/');
//        return string.IsNullOrEmpty(_basePrefix) ? clean : $"{_basePrefix}/{clean}";
//    }


//    public async Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken ct)
//    {
//        var fullKey = BuildKey(key);
//        var put = new PutObjectRequest
//        {
//            BucketName = _bucket,
//            Key = fullKey,
//            InputStream = content,
//            ContentType = contentType,
//            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
//        };
//        var resp = await _s3.PutObjectAsync(put, ct);
//        // you may check resp.ETag if needed
//        return fullKey; // store this in DB
//    }


//    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
//    {
//        var fullKey = BuildKey(key);
//        var resp = await _s3.GetObjectAsync(_bucket, fullKey, ct);
//        return resp.ResponseStream; // caller disposes
//    }


//    public Task DeleteAsync(string key, CancellationToken ct)
//    {
//        var fullKey = BuildKey(key);
//        return _s3.DeleteObjectAsync(_bucket, fullKey, ct);
//    }
//}

using Amazon.S3;
using Amazon.S3.Model;
using DigitalSignServer.Options;
using DigitalSignServer.Storage;
using Microsoft.Extensions.Options;

public sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly string _basePrefix;

    public S3FileStorage(IOptions<S3Options> opts, IAmazonS3 s3)
    {
        _s3 = s3;
        var o = opts.Value;
        if (string.IsNullOrWhiteSpace(o.Bucket))
            throw new InvalidOperationException("S3:Bucket is required");
        _bucket = o.Bucket!;
        _basePrefix = (o.BasePrefix ?? string.Empty).Trim('/');
    }

    private string BuildKey(string key)
    {
        var clean = key.Replace('\\', '/').TrimStart('/');
        return string.IsNullOrEmpty(_basePrefix) ? clean : $"{_basePrefix}/{clean}";
    }

    public async Task<string> SaveAsync(Stream content, string key, string contentType, CancellationToken ct)
    {
        var fullKey = BuildKey(key);
        var put = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = fullKey,
            InputStream = content,
            ContentType = contentType,
            // אם ה-Bucket מוצפן ב-SSE-S3 (ברירת מחדל), אפשר להשאיר ככה.
            // אם ב-KMS, ניתן להוסיף כאן put.ServerSideEncryptionMethod = ServerSideEncryptionMethod.awsKms; ו-SSEKMSKeyId
        };
        await _s3.PutObjectAsync(put, ct);
        return fullKey;
    }
    
    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        var fullKey = BuildKey(key);
        var resp = await _s3.GetObjectAsync(_bucket, fullKey, ct);
        return resp.ResponseStream; // לסגור בצד הקורא
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var fullKey = BuildKey(key);
        return _s3.DeleteObjectAsync(_bucket, fullKey, ct);
    }
}
