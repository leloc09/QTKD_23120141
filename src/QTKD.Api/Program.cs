using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Data;
using QTKD.Api.Dtos;
using QTKD.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Lỗi đọc request (JSON sai, sai kiểu dữ liệu) cũng trả về cùng mẫu ErrorResponse
        options.InvalidModelStateResponseFactory = context =>
        {
            var fields = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .Select(e => e.Key.TrimStart('$', '.'))
                .ToList();
            // Khi body sai JSON, ASP.NET báo thêm lỗi chung cho tham số "request": chỉ giữ lỗi theo trường
            var specific = fields.Where(f => f != "request" && f != "").ToList();
            var errors = specific.Count > 0
                ? specific.Select(f => new FieldError(f, "INVALID_FORMAT", "Giá trị không đúng định dạng.")).ToList()
                : new List<FieldError> { new("body", "REQUIRED", "Thiếu body JSON hoặc body không hợp lệ.") };
            return new BadRequestObjectResult(new ErrorResponse("INVALID_REQUEST", "Request không đọc được.", errors));
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Chuỗi kết nối lấy từ appsettings / User Secrets / biến môi trường, không commit mật khẩu
builder.Services.AddDbContext<QtkdDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("QtkdDb")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<ContractService>();
builder.Services.AddScoped<PricingService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

// Cho phép WebApplicationFactory<Program> dùng trong integration test (tuần 07)
public partial class Program { }
