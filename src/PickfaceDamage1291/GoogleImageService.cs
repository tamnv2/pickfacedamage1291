using System.Text;
using System.Text.Json;

namespace PickfaceDamage1291;

internal sealed record GoogleImagePayload(byte[] Bytes, string MimeType, string FileName);

internal static class GoogleImageService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public static async Task<GoogleImagePayload> DownloadAsync(string fileId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            throw new InvalidOperationException("Ảnh chưa có mã file Google Drive.");
        if (!RuntimeConfigService.IsGoogleGatewayConfigured)
            throw new InvalidOperationException("Kết nối Google chưa sẵn sàng.");

        var session = AppSession.Current ?? throw new InvalidOperationException("Chưa đăng nhập ứng dụng.");
        if (session.OfflineMode)
            throw new InvalidOperationException("Đang offline nên chưa thể tải ảnh từ Google Drive.");

        await FirebaseClient.EnsureFreshAsync(session, ct);
        var body = JsonSerializer.Serialize(new
        {
            action = "get_image",
            id_token = session.IdToken,
            payload = new { file_id = fileId }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, RuntimeConfigService.GoogleGatewayUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Không tải được ảnh Google ({(int)response.StatusCode}).");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (!root.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var error = root.TryGetProperty("error", out var errorNode) ? errorNode.GetString() : null;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Google từ chối tải ảnh." : error);
        }

        var data = root.TryGetProperty("data_base64", out var dataNode) ? dataNode.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(data)) throw new InvalidOperationException("Google không trả dữ liệu ảnh.");
        var mime = root.TryGetProperty("mime_type", out var mimeNode) ? mimeNode.GetString() ?? "application/octet-stream" : "application/octet-stream";
        var name = root.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "image" : "image";
        return new GoogleImagePayload(Convert.FromBase64String(data), mime, name);
    }
}
