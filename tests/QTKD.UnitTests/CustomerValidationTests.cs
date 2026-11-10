using QTKD.Api.Domain;
using QTKD.Api.Dtos;
using QTKD.Api.Services;

namespace QTKD.UnitTests;

// Kiểm tra các trường bắt buộc của Customer (mức nghiệp vụ, không cần database)
public class CustomerValidationTests
{
    [Theory]
    [InlineData("0301234567")]
    [InlineData("0301234567-001")]
    public void Validate_HopLe_KhongCoLoi(string taxCode)
    {
        var errors = CustomerService.Validate(new CustomerRequest("Công ty Minh Phát", taxCode, null, null), out var status);
        Assert.Empty(errors);
        Assert.Equal(CustomerStatus.Active, status); // mặc định Active
    }

    [Fact]
    public void Validate_StatusInactive_DuocChapNhan()
    {
        var errors = CustomerService.Validate(new CustomerRequest("Công ty A", "0301234567", null, "Inactive"), out var status);
        Assert.Empty(errors);
        Assert.Equal(CustomerStatus.Inactive, status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ThieuTen_BaoLoiRequired(string? name)
    {
        var errors = CustomerService.Validate(new CustomerRequest(name, "0301234567", null, null), out _);
        var error = Assert.Single(errors);
        Assert.Equal(("name", "REQUIRED"), (error.Field, error.Code));
    }

    [Fact]
    public void Validate_ThieuCaTenVaMaSoThue_BaoDuHaiLoi()
    {
        var errors = CustomerService.Validate(new CustomerRequest(null, null, null, null), out _);
        Assert.Equal(new[] { "name", "taxCode" }, errors.Select(e => e.Field).ToArray());
        Assert.All(errors, e => Assert.Equal("REQUIRED", e.Code));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("030123456A")]
    [InlineData("0301234567-01")]
    public void Validate_MaSoThueSaiDinhDang_BaoLoi(string taxCode)
    {
        var errors = CustomerService.Validate(new CustomerRequest("Công ty A", taxCode, null, null), out _);
        var error = Assert.Single(errors);
        Assert.Equal(("taxCode", "INVALID_FORMAT"), (error.Field, error.Code));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("1")]
    [InlineData("Deleted")]
    public void Validate_TrangThaiKhongHopLe_BaoLoi(string status)
    {
        var errors = CustomerService.Validate(new CustomerRequest("Công ty A", "0301234567", null, status), out _);
        var error = Assert.Single(errors);
        Assert.Equal(("status", "INVALID_VALUE"), (error.Field, error.Code));
    }

    [Fact]
    public void Validate_TenQuaDai_BaoLoi()
    {
        var errors = CustomerService.Validate(new CustomerRequest(new string('A', 201), "0301234567", null, null), out _);
        Assert.Equal("TOO_LONG", Assert.Single(errors).Code);
    }
}
