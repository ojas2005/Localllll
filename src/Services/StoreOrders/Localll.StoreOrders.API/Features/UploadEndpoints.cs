using System.Security.Cryptography;

namespace Localll.StoreOrders.API.Features;

/// <summary>
/// Local file uploads for payment screenshots. Saves under wwwroot/uploads and
/// serves them statically (anonymously, so admin img tags can load them). In
/// production this would push to object storage and return a signed URL instead.
/// </summary>
public static class UploadEndpoints
{
    private static readonly string[] Allowed = [".jpg", ".jpeg", ".png", ".pdf"];
    private const long MaxBytes = 10 * 1024 * 1024;

    public static void MapUploadEndpoints(this IEndpointRouteBuilder app, string uploadsRoot)
    {
        app.MapPost("/api/v1/store/uploads", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType) return Results.BadRequest(new { error = "Expected multipart/form-data." });
            var form = await http.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "No file provided." });
            if (file.Length > MaxBytes) return Results.BadRequest(new { error = "File must be under 10 MB." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!Allowed.Contains(ext))
                return Results.BadRequest(new { error = "Only JPG, PNG or PDF files are allowed." });

            Directory.CreateDirectory(uploadsRoot);
            var name = $"{DateTime.UtcNow:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}{ext}";
            var path = Path.Combine(uploadsRoot, name);
            await using (var stream = File.Create(path))
                await file.CopyToAsync(stream);

            // Gateway-relative URL so the SPA (and admin preview) can load it directly.
            return Results.Ok(new { url = $"/api/v1/store/uploads/{name}", fileName = file.FileName });
        }).RequireAuthorization().WithTags("Store Uploads")
          .DisableAntiforgery();
    }
}
