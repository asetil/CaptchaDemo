using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Drawing.Processing;

[Route("captcha")]
[ApiController]
public class CaptchaController : ControllerBase
{
    private readonly IDistributedCache _cache;
    private readonly Random _rand = new();

    private static readonly SixLabors.Fonts.Font CaptchaFont = SystemFonts.CreateFont("Arial", 36, FontStyle.Bold);
    private static readonly SolidBrush NoiseBrush = new SolidBrush(Color.Gray);

    public CaptchaController(IDistributedCache cache)
    {
        _cache = cache;
    }

    [HttpGet("generate")]
    public async Task<IActionResult> Generate()
    {
        string code = GenerateRandomCode(5);
        string key = Guid.NewGuid().ToString();

        await _cache.SetStringAsync(key, code,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(3)
            });

        byte[] image = await GenerateImageAsync(code);

        string base64 = $"data:image/png;base64,{Convert.ToBase64String(image)}";

        return Ok(new
        {
            token = key,
            imageBase64 = base64
        });
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] CaptchaValidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.UserInput))
            return BadRequest("Token or input missing.");

        var expected = await _cache.GetStringAsync(request.Token);
        if (expected == null)
            return BadRequest("Invalid token or expired.");

        if (string.Equals(expected, request.UserInput, StringComparison.OrdinalIgnoreCase))
        {
            await _cache.RemoveAsync(request.Token);
            return Ok(new { success = true });
        }
        else
            return BadRequest(new { success = false });
    }


    private string GenerateRandomCode(int length)
    {
        const string chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, length)
            .Select(_ => chars[_rand.Next(chars.Length)]).ToArray());
    }

    private async Task<byte[]> GenerateImageAsync(string code)
    {
        int width = 200, height = 80;
        var rand = new Random();

        using var image = new Image<Rgba32>(width, height);

        image.Mutate(ctx =>
        {
            ctx.Fill(Color.LightGray);

            // Rastgele çizgiler
            for (int i = 0; i < 6; i++)
            {
                var path = new PathBuilder();
                path.AddLines(new PointF[]
                {
                new PointF(rand.Next(width), rand.Next(height)),
                new PointF(rand.Next(width), rand.Next(height))
                });
                ctx.Draw(NoiseBrush, 1, path.Build());
            }

            // Rastgele daireler
            for (int i = 0; i < 10; i++)
            {
                int radius = rand.Next(3, 8);
                int x = rand.Next(width - radius);
                int y = rand.Next(height - radius);
                ctx.Draw(NoiseBrush, 1, new EllipsePolygon(x + radius, y + radius, radius));
            }

            // CAPTCHA metni (okunması biraz zor)
            float xPos = 10;
            var textColor = Color.DarkSlateGray; // biraz açık, okunması zor

            foreach (char c in code)
            {
                float yPos = 15 + rand.Next(-5, 5); // dikey sapma
                ctx.DrawText(c.ToString(), CaptchaFont, textColor, new PointF(xPos, yPos));
                xPos += 30;
            }

            // Hafif blur ile zorlaştır
            ctx.GaussianBlur(0.7f);
        });

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
}

public class CaptchaGenerateResponse
{
    public string Token { get; set; }
    public string ImageBase64 { get; set; }
}

public class CaptchaValidateRequest
{
    public string Token { get; set; }
    public string UserInput { get; set; }
}