using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.CurrentUser;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Contracts;

public interface IContractService
{
    Task<ContractResponse> CreateAsync(CreateContractRequest request, CancellationToken ct);

    Task<ContractResponse> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ContractResponse>> ListAsync(Guid? customerId, Guid? quoteId, ContractStatus? status, CancellationToken ct);

    /// <summary>Records the signature and starts the installation (state Planned) in one transaction.</summary>
    Task<ContractResponse> SignAsync(Guid id, SignContractRequest request, CancellationToken ct);

    Task<ContractResponse> RecordDepositAsync(Guid id, RecordDepositRequest request, CancellationToken ct);

    Task<IReadOnlyList<ContractAuditEntryResponse>> AuditAsync(Guid id, CancellationToken ct);
}

public sealed class ContractService(AppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IContractService
{
    public async Task<ContractResponse> CreateAsync(CreateContractRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var quoteId = request.QuoteId!.Value;

        var quote = await db.Set<Quote>().AsNoTracking().Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            ?? throw new RequestValidationException(nameof(CreateContractRequest.QuoteId), "Quote does not exist.");
        if (quote.Status != QuoteStatus.Accepted)
        {
            throw new DomainException($"A contract can be created only from an accepted quote (the quote is {quote.Status}).");
        }

        if (await db.Set<Contract>().AnyAsync(c => c.QuoteId == quoteId, ct))
        {
            throw new DomainException("A contract already exists for this quote.");
        }

        var customer = await db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == quote.CustomerId, ct)
            ?? throw new DomainException("The customer of the quote no longer exists.");
        customer.EnsureActive();

        var lines = quote.Lines
            .OrderBy(l => l.Position)
            .Select(l => new ContractLineData(l.ComponentId, l.ComponentType, l.Code, l.Name, l.UnitPrice, l.Quantity, l.NetPrice))
            .ToList();
        var contract = Contract.Create(
            quote.CustomerId,
            quote.Id,
            quote.VatRatePercent,
            lines,
            new QuoteTotals(quote.NetTotal, quote.VatTotal, quote.GrossTotal),
            userId,
            timeProvider.GetUtcNow());

        db.Set<Contract>().Add(contract);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request created the contract of this quote after our pre-check.
            throw new DomainException("A contract already exists for this quote.");
        }

        return ContractResponse.FromEntity(contract);
    }

    public async Task<ContractResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var contract = await db.Set<Contract>().AsNoTracking().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("Contract", id);
        return ContractResponse.FromEntity(contract);
    }

    public async Task<IReadOnlyList<ContractResponse>> ListAsync(Guid? customerId, Guid? quoteId, ContractStatus? status, CancellationToken ct)
    {
        var query = db.Set<Contract>().AsNoTracking().Include(c => c.Lines).AsQueryable();
        if (customerId is { } customer)
        {
            query = query.Where(c => c.CustomerId == customer);
        }

        if (quoteId is { } quote)
        {
            query = query.Where(c => c.QuoteId == quote);
        }

        if (status is { } s)
        {
            query = query.Where(c => c.Status == s);
        }

        var contracts = await query.OrderByDescending(c => c.Sequence).ToListAsync(ct);
        return contracts.ConvertAll(ContractResponse.FromEntity);
    }

    public async Task<ContractResponse> SignAsync(Guid id, SignContractRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow();
        var contract = await FindForUpdateAsync(id, ct);

        var installation = Installation.Create(
            contract.CustomerId,
            contract.Id,
            request.SupplyPointId!.Value,
            string.IsNullOrWhiteSpace(request.InstallationName) ? $"Inštalácia {contract.Number}" : request.InstallationName,
            plannedPowerKwp: null,
            note: null,
            userId,
            now);
        contract.Sign(request.SignedOn ?? Today(now), installation.Id, userId, Today(now), now);

        db.Add(installation);
        await SaveAsync(ct);
        return ContractResponse.FromEntity(contract);
    }

    public async Task<ContractResponse> RecordDepositAsync(Guid id, RecordDepositRequest request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var now = timeProvider.GetUtcNow();
        var contract = await FindForUpdateAsync(id, ct);

        contract.RecordDeposit(request.Amount!.Value, request.PaidOn ?? Today(now), userId, Today(now), now);

        await SaveAsync(ct);
        return ContractResponse.FromEntity(contract);
    }

    public async Task<IReadOnlyList<ContractAuditEntryResponse>> AuditAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Set<Contract>().AnyAsync(c => c.Id == id, ct))
        {
            throw new NotFoundException("Contract", id);
        }

        var entries = await db.Set<ContractAuditEntry>().AsNoTracking()
            .Where(e => e.ContractId == id)
            .OrderBy(e => e.PerformedAt).ThenBy(e => e.Id)
            .ToListAsync(ct);
        return entries.ConvertAll(ContractAuditEntryResponse.FromEntity);
    }

    private static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);

    private async Task<Contract> FindForUpdateAsync(Guid id, CancellationToken ct) =>
        await db.Set<Contract>().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw new NotFoundException("Contract", id);

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || (ex is DbUpdateException dbEx && dbEx.IsUniqueViolation()))
        {
            // The contract was changed by a concurrent request, or its installation already exists.
            throw new DomainException("The contract was changed by another request. Reload it and try again.");
        }
    }
}
