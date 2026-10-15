using Restaurantes.RestaurantOperations.Contracts.Events;
using Restaurantes.RestaurantOperations.Contracts.Requests;
using Restaurantes.RestaurantOperations.Contracts.Responses;
using Restaurantes.RestaurantOperations.Domain;

namespace Restaurantes.RestaurantOperations.Application;

public sealed class CompanyCommandService(ICompanyWriteStore store, TimeProvider timeProvider)
{
    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        await store.FindAsync(id, cancellationToken) is not null;

    public async Task<CompanyResponse?> CreateAsync(
        CreateCompanyRequest request,
        CancellationToken cancellationToken
    )
    {
        string taxId = NormalizeTaxId(request.TaxId);
        if (await store.TaxIdExistsAsync(taxId, null, cancellationToken))
        {
            return null;
        }

        Company company = Company.Create(
            request.LegalName,
            taxId,
            request.FiscalAddress,
            request.Phone,
            request.Email,
            timeProvider.GetUtcNow().UtcDateTime
        );
        await store.SaveWithEventAsync(company, ToEvent(company), cancellationToken);
        return ToResponse(company);
    }

    public async Task<CompanyResponse?> UpdateAsync(
        Guid id,
        UpdateCompanyRequest request,
        CancellationToken cancellationToken
    )
    {
        Company? company = await store.FindAsync(id, cancellationToken);
        if (company is null)
        {
            return null;
        }

        string taxId = NormalizeTaxId(request.TaxId);
        if (await store.TaxIdExistsAsync(taxId, id, cancellationToken))
        {
            return null;
        }

        company.Update(
            request.LegalName,
            taxId,
            request.FiscalAddress,
            request.Phone,
            request.Email,
            timeProvider.GetUtcNow().UtcDateTime
        );
        await store.SaveWithEventAsync(company, ToEvent(company), cancellationToken);
        return ToResponse(company);
    }

    private static string NormalizeTaxId(string value) =>
        value.Replace(" ", "").Trim().ToUpperInvariant();

    private static CompanyChanged ToEvent(Company company) =>
        new(
            company.Id,
            company.LegalName,
            company.TaxId,
            company.FiscalAddress,
            company.Phone,
            company.Email,
            company.Version,
            company.UpdatedAtUtc
        );

    private static CompanyResponse ToResponse(Company company) =>
        new(
            company.Id,
            company.LegalName,
            company.TaxId,
            company.FiscalAddress,
            company.Phone,
            company.Email,
            company.Version,
            company.UpdatedAtUtc
        );
}
