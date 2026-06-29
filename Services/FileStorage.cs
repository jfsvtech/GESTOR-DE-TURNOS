namespace GeneradorTurnos.Services;

public interface IFileStorage
{
    /// <summary>Guarda una imagen y devuelve su URL publica. En produccion se recomienda reemplazar Local por S3/Cloudinary.</summary>
    Task<string?> GuardarImagenAsync(IFormFile? file, string carpeta);
}

public class FileStorage : IFileStorage
{
    private static readonly Dictionary<string, string[]> Firmas = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ["FF-D8-FF"],
        [".jpeg"] = ["FF-D8-FF"],
        [".png"] = ["89-50-4E-47-0D-0A-1A-0A"],
        [".webp"] = ["52-49-46-46"],
        [".gif"] = ["47-49-46-38"]
    };

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ILogger<FileStorage> _logger;

    public FileStorage(IWebHostEnvironment env, IConfiguration config, ILogger<FileStorage> logger)
    {
        _env = env;
        _config = config;
        _logger = logger;
    }

    public async Task<string?> GuardarImagenAsync(IFormFile? file, string carpeta)
    {
        if (file is null || file.Length == 0) return null;

        var maxMb = Math.Clamp(_config.GetValue("Storage:MaxImageMb", 5), 1, 15);
        if (file.Length > maxMb * 1024L * 1024L) return null;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Firmas.ContainsKey(ext)) return null;
        if (!await TieneFirmaValidaAsync(file, ext)) return null;

        var provider = _config.GetValue("Storage:Provider", "Local");
        if (!provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Storage:Provider={Provider} configurado, pero no hay adaptador cloud activo. Se usa almacenamiento local temporal.", provider);
        }

        var safeFolder = string.Join('/',
            carpeta.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Select(p => string.Concat(p.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))))
            .Trim('/');
        if (string.IsNullOrWhiteSpace(safeFolder)) return null;

        var rel = Path.Combine("uploads", safeFolder);
        var root = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var dir = Path.GetFullPath(Path.Combine(root, rel));
        var uploadRoot = Path.GetFullPath(Path.Combine(root, "uploads"));
        if (!dir.StartsWith(uploadRoot, StringComparison.OrdinalIgnoreCase)) return null;

        Directory.CreateDirectory(dir);

        var nombre = $"{Guid.NewGuid():N}{ext}";
        var ruta = Path.Combine(dir, nombre);
        await using (var fs = new FileStream(ruta, FileMode.CreateNew))
            await file.CopyToAsync(fs);

        return $"/uploads/{safeFolder}/{nombre}".Replace("\\", "/");
    }

    private static async Task<bool> TieneFirmaValidaAsync(IFormFile file, string ext)
    {
        var buffer = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(buffer);
        var hex = BitConverter.ToString(buffer, 0, read);
        return Firmas[ext].Any(hex.StartsWith);
    }
}
