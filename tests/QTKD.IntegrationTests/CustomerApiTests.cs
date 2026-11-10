using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using QTKD.Api.Domain;
using QTKD.Api.Dtos;
using QTKD.IntegrationTests.Infrastructure;

namespace QTKD.IntegrationTests;

public class CustomerApiTests(QtkdApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task TC01_TaoCustomerHopLe_TaoDuocVaDocLaiTuDb()
    {
        var taxCode = NewTaxCode();
        var response = await Client.PostAsJsonAsync("/api/customers",
            new CustomerRequest("  Công ty Minh Phát TC01  ", taxCode, "12 Nguyễn Huệ, TP.HCM", null));

        // HTTP + body
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsync<CustomerResponse>(response);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Công ty Minh Phát TC01", created.Name); // đã cắt khoảng trắng
        Assert.Equal(taxCode, created.TaxCode);
        Assert.Equal("Active", created.Status);
        Assert.Equal($"/api/customers/{created.Id}", response.Headers.Location?.AbsolutePath, ignoreCase: true);

        // Đọc lại qua API
        var fetched = await Client.GetFromJsonAsync<CustomerResponse>($"/api/customers/{created.Id}", Json);
        Assert.Equal(created, fetched);

        // Đọc lại trực tiếp từ DB
        await using var db = Db();
        var row = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal("Công ty Minh Phát TC01", row.Name);
        Assert.Equal(taxCode, row.TaxCode);
        Assert.Equal("12 Nguyễn Huệ, TP.HCM", row.Address);
        Assert.Equal(CustomerStatus.Active, row.Status);

        // Có trong danh sách
        var list = await Client.GetFromJsonAsync<List<CustomerResponse>>("/api/customers", Json);
        Assert.Contains(list!, c => c.Id == created.Id);
    }

    [Fact]
    public async Task TC02_TaoCustomerThieuThongTinBatBuoc_BiTuChoiCoThongBaoLoi()
    {
        await using var db = Db();
        var before = await db.Customers.CountAsync();

        var response = await Client.PostAsJsonAsync("/api/customers", new CustomerRequest("   ", null, null, null));

        var error = await ReadErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("VALIDATION_FAILED", error.Code);
        Assert.Contains(error.Errors, e => e is { Field: "name", Code: "REQUIRED" } && e.Message.Length > 0);
        Assert.Contains(error.Errors, e => e is { Field: "taxCode", Code: "REQUIRED" } && e.Message.Length > 0);
        Assert.Equal(before, await db.Customers.CountAsync()); // không ghi gì vào DB
    }

    [Fact]
    public async Task TC02b_TaoCustomerKhongCoBody_BiTuChoiCungMauLoi()
    {
        var response = await Client.PostAsync("/api/customers",
            new StringContent("{ khong phai json", System.Text.Encoding.UTF8, "application/json"));

        var error = await ReadErrorAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("INVALID_REQUEST", error.Code);
        Assert.NotEmpty(error.Errors);
    }

    [Fact]
    public async Task TaoCustomerTrungMaSoThue_BiTuChoi409()
    {
        var first = await CreateCustomerAsync();
        await using var db = Db();
        var before = await db.Customers.CountAsync();

        var response = await Client.PostAsJsonAsync("/api/customers",
            new CustomerRequest("Công ty khác", first.TaxCode, null, null));

        var error = await ReadErrorAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("CUSTOMER_TAXCODE_EXISTS", error.Code);
        Assert.Equal(before, await db.Customers.CountAsync());
    }

    [Fact]
    public async Task CapNhatCustomer_LuuVaoDb()
    {
        var customer = await CreateCustomerAsync();

        var response = await Client.PutAsJsonAsync($"/api/customers/{customer.Id}",
            new CustomerRequest("Tên mới", customer.TaxCode, "Hà Nội", "Inactive"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Inactive", (await ReadAsync<CustomerResponse>(response)).Status);
        await using var db = Db();
        var row = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(("Tên mới", "Hà Nội", CustomerStatus.Inactive), (row.Name, row.Address, row.Status));
    }

    [Fact]
    public async Task XemCustomerKhongTonTai_404()
    {
        var error = await ReadErrorAsync(await Client.GetAsync($"/api/customers/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
        Assert.Equal("CUSTOMER_NOT_FOUND", error.Code);
    }
}
