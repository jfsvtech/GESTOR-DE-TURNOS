namespace GeneradorTurnos.Services;

public interface IFileStorage
{
    /// <summary>Guarda una imagen en wwwroot/uploads/{carpeta} y devuelve su URL pública (/uploads/...).
    /// Devuelve null si no hay archivo o no es una imagen válida.</summary>
    Task<string?> GuardarImagenAsync(IFormFile? file, string carpeta);
}

public class FileStorage : IFileStorage
{
    private static readonly string[] Permitidas = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private readonly IWebHostEnvironment _env;

    public FileStorage(IWebHostEnvironment env) => _env = env;

    public async Task<string?> GuardarImagenAsync(IFormFile? file, string carpeta)
    {
        if (file is null || file.Length == 0) return null;
        if (file.Length > 8 * 1024 * 1024) return null; // máx 8 MB

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Permitidas.Contains(ext)) return null;

        var rel = Path.Combine("uploads", carpeta);
        var dir = Path.Combine(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"), rel);
        Directory.CreateDirectory(dir);

        var nombre = $"{Guid.NewGuid():N}{ext}";
        var ruta = Path.Combine(dir, nombre);
        await using (var fs = new FileStream(ruta, FileMode.Create))
            await file.CopyToAsync(fs);

        return $"/uploads/{carpeta}/{nombre}".Replace("\\", "/");
    }
}
