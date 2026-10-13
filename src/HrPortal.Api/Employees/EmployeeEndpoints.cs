using HrPortal.Api.Auth;
using HrPortal.Domain.Common;
using HrPortal.Domain.Employees;
using HrPortal.Domain.Organisation;
using HrPortal.Domain.Security;
using HrPortal.Infrastructure.Persistence;
using HrPortal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace HrPortal.Api.Employees;

/// <summary>
/// Employee records (EMP-01, 02, 04, 05, 08). Every query is scoped by the permission matrix: HR sees their
/// countries, managers their team's work profile, everyone their own record. Personal details are limited to
/// HR and the employee (see <see cref="UserAccess.PersonalDataScopesFor"/>).
/// </summary>
public static class EmployeeEndpoints
{
    public sealed record EmployeeSummary(
        Guid Id,
        string EmployeeNumber,
        string FullName,
        string Designation,
        string WorkEmail,
        string CountryCode,
        Guid DepartmentId,
        string DepartmentName,
        Guid OfficeLocationId,
        string OfficeLocationName,
        Guid? ManagerId,
        string? ManagerName,
        EmploymentStatus Status,
        EmploymentType EmploymentType);

    public sealed record EmergencyContactDto(string Name, string Relationship, string Phone);

    public sealed record PersonalDetailsDto(
        string? PersonalEmail,
        string? Phone,
        DateOnly? DateOfBirth,
        string? AddressLine1,
        string? AddressLine2,
        string? City,
        string? Region,
        string? PostalCode,
        string? AddressCountryCode,
        string? NationalId,
        IReadOnlyList<EmergencyContactDto> EmergencyContacts);

    /// <summary>What the signed-in user may do with this record; the UI hides controls with it, the API enforces it.</summary>
    public sealed record EmployeePermissions(
        bool CanEdit,
        bool CanViewPersonal,
        bool CanEditPersonal,
        bool CanViewCompensation,
        bool CanEditCompensation,
        bool CanManageDocuments,
        bool CanTerminate);

    public sealed record EmployeeDetail(
        Guid Id,
        string EmployeeNumber,
        string GivenName,
        string FamilyName,
        string Designation,
        string WorkEmail,
        string CountryCode,
        Guid LegalEntityId,
        string LegalEntityName,
        Guid OfficeLocationId,
        string OfficeLocationName,
        Guid DepartmentId,
        string DepartmentName,
        Guid? GradeId,
        string? GradeName,
        Guid? ManagerId,
        string? ManagerName,
        EmploymentType EmploymentType,
        EmploymentStatus Status,
        IReadOnlyList<EmploymentStatus> NextStatuses,
        DateOnly JoiningDate,
        DateOnly? ProbationEndDate,
        DateOnly? LastWorkingDate,
        PersonalDetailsDto? Personal,
        EmployeePermissions Permissions);

    public sealed record CreateEmployeeRequest(
        string GivenName,
        string FamilyName,
        string EmployeeNumber,
        string WorkEmail,
        EmploymentType EmploymentType,
        string Designation,
        Guid LegalEntityId,
        Guid OfficeLocationId,
        Guid DepartmentId,
        Guid? GradeId,
        Guid? ManagerId,
        DateOnly JoiningDate,
        PersonalDetailsDto? Personal);

    public sealed record UpdatePersonalRequest(string GivenName, string FamilyName, PersonalDetailsDto Personal);

    public sealed record JobChangeRequest(
        JobChangeKind Kind,
        DateOnly EffectiveDate,
        string Designation,
        Guid LegalEntityId,
        Guid OfficeLocationId,
        Guid DepartmentId,
        Guid? GradeId,
        string? Reason);

    public sealed record ManagerChangeRequest(Guid? ManagerId, DateOnly EffectiveDate, string? Reason);

    public sealed record StatusChangeRequest(EmploymentStatus Status, DateOnly EffectiveDate, DateOnly? LastWorkingDate, string? Reason);

    public sealed record HistoryEntryDto(
        Guid Id,
        JobChangeKind Kind,
        DateOnly EffectiveDate,
        string? Reason,
        string Designation,
        string DepartmentName,
        string OfficeLocationName,
        string LegalEntityName,
        string? GradeName,
        string? ManagerName,
        EmploymentStatus Status);

    public static IEndpointRouteBuilder MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/employees").WithTags("Employees");
        var view = Policies.Module(HrModule.EmployeeRecords, Capability.View);
        var create = Policies.Module(HrModule.EmployeeRecords, Capability.Create);
        var edit = Policies.Module(HrModule.EmployeeRecords, Capability.Edit);

        group.MapGet("/", ListAsync).RequireAuthorization(view);
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization(view);
        group.MapPost("/", CreateAsync).RequireAuthorization(create);
        group.MapPut("/{id:guid}/personal", UpdatePersonalAsync).RequireAuthorization(edit);
        group.MapPost("/{id:guid}/job-changes", ChangeJobAsync).RequireAuthorization(edit);
        group.MapPost("/{id:guid}/manager", ChangeManagerAsync).RequireAuthorization(edit);
        group.MapPost("/{id:guid}/status", ChangeStatusAsync).RequireAuthorization(edit);
        group.MapGet("/{id:guid}/history", HistoryAsync).RequireAuthorization(view);

        return app;
    }

    /// <summary>EMP-08: directory with search and filters, always within the user's scope.</summary>
    private static async Task<List<EmployeeSummary>> ListAsync(
        string? search,
        Guid? departmentId,
        Guid? officeLocationId,
        Guid? managerId,
        string? countryCode,
        EmploymentStatus[]? status,
        HrPortalDbContext db,
        IAccessContext accessContext,
        CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var query = db.Employees.AsNoTracking().VisibleTo(db, access, HrModule.EmployeeRecords, Capability.View);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.Person.GivenName + " " + e.Person.FamilyName, pattern)
                || EF.Functions.ILike(e.EmployeeNumber, pattern)
                || EF.Functions.ILike(e.WorkEmail, pattern)
                || EF.Functions.ILike(e.Designation, pattern));
        }

        if (departmentId is { } department)
        {
            query = query.Where(e => e.DepartmentId == department);
        }

        if (officeLocationId is { } office)
        {
            query = query.Where(e => e.OfficeLocationId == office);
        }

        if (managerId is { } manager)
        {
            query = query.Where(e => e.ManagerId == manager);
        }

        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var country = countryCode.Trim().ToUpperInvariant();
            query = query.Where(e => e.CountryCode == country);
        }

        if (status is { Length: > 0 })
        {
            query = query.Where(e => status.Contains(e.Status));
        }

        return await query
            .OrderBy(e => e.Person.GivenName).ThenBy(e => e.Person.FamilyName).ThenBy(e => e.EmployeeNumber)
            .Select(e => new EmployeeSummary(
                e.Id,
                e.EmployeeNumber,
                e.Person.GivenName + " " + e.Person.FamilyName,
                e.Designation,
                e.WorkEmail,
                e.CountryCode,
                e.DepartmentId,
                e.Department.Name,
                e.OfficeLocationId,
                e.OfficeLocation.Name,
                e.ManagerId,
                e.Manager == null ? null : e.Manager.Person.GivenName + " " + e.Manager.Person.FamilyName,
                e.Status,
                e.EmploymentType))
            .ToListAsync(ct);
    }

    private static async Task<IResult> GetAsync(Guid id, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (await EmployeeScope.DenyAsync(db, access, id, access.ScopesFor(HrModule.EmployeeRecords, Capability.View), ct) is { } denied)
        {
            return denied;
        }

        return Results.Ok(await LoadDetailAsync(db, access, id, ct));
    }

    private static async Task<IResult> CreateAsync(
        CreateEmployeeRequest request, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        var placement = await LoadPlacementAsync(db, request.Designation, request.LegalEntityId, request.OfficeLocationId, request.DepartmentId, request.GradeId, ct);
        if (!EmployeeScope.CanPlaceIn(access, Capability.Create, placement.LegalEntity.CountryCode))
        {
            return Results.Forbid();
        }

        var manager = request.ManagerId is { } managerId ? await LoadManagerAsync(db, managerId, ct) : null;
        var country = await db.CountrySettings.SingleAsync(c => c.CountryCode == placement.LegalEntity.CountryCode, ct);
        var today = Today(clock);

        var person = new Person(request.GivenName, request.FamilyName);
        person.UpdatePersonalDetails(ToDomain(request.Personal), today);
        var employee = Employee.Create(
            person, request.EmployeeNumber, request.WorkEmail, request.EmploymentType, placement, manager,
            request.JoiningDate, EmploymentTerms.For(country, placement.Grade));

        db.People.Add(person);
        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/employees/{employee.Id}", await LoadDetailAsync(db, access, employee.Id, ct));
    }

    private static async Task<IResult> UpdatePersonalAsync(
        Guid id, UpdatePersonalRequest request, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (await EmployeeScope.DenyAsync(db, access, id, access.PersonalDataScopesFor(Capability.Edit), ct) is { } denied)
        {
            return denied;
        }

        var person = (await db.Employees.Include(e => e.Person).ThenInclude(p => p.EmergencyContacts).SingleAsync(e => e.Id == id, ct)).Person;
        person.Rename(request.GivenName, request.FamilyName);
        person.UpdatePersonalDetails(ToDomain(request.Personal), Today(clock));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadDetailAsync(db, access, id, ct));
    }

    private static async Task<IResult> ChangeJobAsync(
        Guid id, JobChangeRequest request, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (await EmployeeScope.DenyAsync(db, access, id, access.ScopesFor(HrModule.EmployeeRecords, Capability.Edit), ct) is { } denied)
        {
            return denied;
        }

        var placement = await LoadPlacementAsync(db, request.Designation, request.LegalEntityId, request.OfficeLocationId, request.DepartmentId, request.GradeId, ct);
        if (!EmployeeScope.CanPlaceIn(access, Capability.Edit, placement.LegalEntity.CountryCode))
        {
            return Results.Forbid();
        }

        var employee = await db.Employees.SingleAsync(e => e.Id == id, ct);
        employee.ChangeJob(request.Kind, placement, request.EffectiveDate, request.Reason, Today(clock));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadDetailAsync(db, access, id, ct));
    }

    private static async Task<IResult> ChangeManagerAsync(
        Guid id, ManagerChangeRequest request, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (await EmployeeScope.DenyAsync(db, access, id, access.ScopesFor(HrModule.EmployeeRecords, Capability.Edit), ct) is { } denied)
        {
            return denied;
        }

        var employee = await db.Employees.SingleAsync(e => e.Id == id, ct);
        var manager = request.ManagerId is { } managerId ? await LoadManagerAsync(db, managerId, ct) : null;
        var loop = manager is not null && await db.ReportIds(id).AnyAsync(r => r == manager.Id, ct);
        employee.ChangeManager(manager, loop, request.EffectiveDate, request.Reason, Today(clock));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadDetailAsync(db, access, id, ct));
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid id, StatusChangeRequest request, HrPortalDbContext db, IAccessContext accessContext, TimeProvider clock, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (await EmployeeScope.DenyAsync(db, access, id, access.ScopesFor(HrModule.EmployeeRecords, Capability.Edit), ct) is { } denied)
        {
            return denied;
        }

        if (request.Status == EmploymentStatus.Terminated && !access.CanTerminate)
        {
            return Results.Forbid();
        }

        var employee = await db.Employees.Include(e => e.Grade).SingleAsync(e => e.Id == id, ct);
        var country = await db.CountrySettings.SingleAsync(c => c.CountryCode == employee.CountryCode, ct);
        employee.ChangeStatus(
            request.Status, request.EffectiveDate, request.LastWorkingDate, EmploymentTerms.For(country, employee.Grade), request.Reason, Today(clock));
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadDetailAsync(db, access, id, ct));
    }

    /// <summary>EMP-05: dated job and status history, newest first.</summary>
    private static async Task<IResult> HistoryAsync(Guid id, HrPortalDbContext db, IAccessContext accessContext, CancellationToken ct)
    {
        var access = await accessContext.GetAsync(ct);
        if (await EmployeeScope.DenyAsync(db, access, id, access.ScopesFor(HrModule.EmployeeRecords, Capability.View), ct) is { } denied)
        {
            return denied;
        }

        var history = await (
                from h in db.JobHistory.AsNoTracking()
                where h.EmployeeId == id
                join d in db.Departments on h.DepartmentId equals d.Id
                join o in db.OfficeLocations on h.OfficeLocationId equals o.Id
                join l in db.LegalEntities on h.LegalEntityId equals l.Id
                from g in db.Grades.Where(g => g.Id == h.GradeId).DefaultIfEmpty()
                from m in db.Employees.Where(m => m.Id == h.ManagerId).DefaultIfEmpty()
                orderby h.EffectiveDate descending, h.Id descending
                select new HistoryEntryDto(
                    h.Id, h.Kind, h.EffectiveDate, h.Reason, h.Designation, d.Name, o.Name, l.Name,
                    g == null ? null : g.Name,
                    m == null ? null : m.Person.GivenName + " " + m.Person.FamilyName,
                    h.Status))
            .ToListAsync(ct);
        return Results.Ok(history);
    }

    private static async Task<EmployeeDetail> LoadDetailAsync(HrPortalDbContext db, UserAccess access, Guid id, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking()
            .Include(x => x.Person).ThenInclude(p => p.EmergencyContacts)
            .Include(x => x.LegalEntity)
            .Include(x => x.OfficeLocation)
            .Include(x => x.Department)
            .Include(x => x.Grade)
            .Include(x => x.Manager!.Person)
            .AsSplitQuery()
            .SingleAsync(x => x.Id == id, ct);

        var canEdit = await EmployeeScope.CoversAsync(db, access, id, access.ScopesFor(HrModule.EmployeeRecords, Capability.Edit), ct);
        var canViewPersonal = await EmployeeScope.CoversAsync(db, access, id, access.PersonalDataScopesFor(Capability.View), ct);
        var canEditPersonal = await EmployeeScope.CoversAsync(db, access, id, access.PersonalDataScopesFor(Capability.Edit), ct);
        var permissions = new EmployeePermissions(
            canEdit,
            canViewPersonal,
            canEditPersonal,
            await EmployeeScope.CoversAsync(db, access, id, access.ScopesFor(HrModule.Compensation, Capability.View), ct),
            await EmployeeScope.CoversAsync(db, access, id, access.ScopesFor(HrModule.Compensation, Capability.Create), ct),
            canEditPersonal,
            canEdit && access.CanTerminate);

        var p = e.Person;
        var personal = canViewPersonal
            ? new PersonalDetailsDto(
                p.PersonalEmail, p.Phone, p.DateOfBirth, p.AddressLine1, p.AddressLine2, p.City, p.Region, p.PostalCode,
                p.AddressCountryCode, p.NationalId,
                p.EmergencyContacts.Select(c => new EmergencyContactDto(c.Name, c.Relationship, c.Phone)).ToList())
            : null;

        return new EmployeeDetail(
            e.Id, e.EmployeeNumber, p.GivenName, p.FamilyName, e.Designation, e.WorkEmail, e.CountryCode,
            e.LegalEntityId, e.LegalEntity.Name, e.OfficeLocationId, e.OfficeLocation.Name, e.DepartmentId, e.Department.Name,
            e.GradeId, e.Grade?.Name, e.ManagerId, e.Manager?.Person.FullName,
            e.EmploymentType, e.Status, canEdit ? EmploymentStatusRules.NextStatuses(e.Status) : [],
            e.JoiningDate, e.ProbationEndDate, e.LastWorkingDate, personal, permissions);
    }

    private static async Task<JobPlacement> LoadPlacementAsync(
        HrPortalDbContext db, string designation, Guid legalEntityId, Guid officeLocationId, Guid departmentId, Guid? gradeId, CancellationToken ct)
    {
        var legalEntity = await db.LegalEntities.SingleOrDefaultAsync(x => x.Id == legalEntityId && x.IsActive, ct)
            ?? throw new DomainValidationException(nameof(legalEntityId), "Choose an active legal entity.");
        var office = await db.OfficeLocations.SingleOrDefaultAsync(x => x.Id == officeLocationId && x.IsActive, ct)
            ?? throw new DomainValidationException(nameof(officeLocationId), "Choose an active office.");
        var department = await db.Departments.SingleOrDefaultAsync(x => x.Id == departmentId && x.IsActive, ct)
            ?? throw new DomainValidationException(nameof(departmentId), "Choose an active department.");
        var grade = gradeId is { } g
            ? await db.Grades.SingleOrDefaultAsync(x => x.Id == g && x.IsActive, ct)
                ?? throw new DomainValidationException(nameof(gradeId), "Choose an active grade.")
            : null;
        return new JobPlacement(designation, legalEntity, office, department, grade);
    }

    private static async Task<Employee> LoadManagerAsync(HrPortalDbContext db, Guid managerId, CancellationToken ct) =>
        await db.Employees.SingleOrDefaultAsync(e => e.Id == managerId, ct)
            ?? throw new DomainValidationException(nameof(managerId), "Choose an existing employee as manager.");

    private static PersonalDetails ToDomain(PersonalDetailsDto? dto) =>
        dto is null
            ? PersonalDetails.Empty
            : new PersonalDetails(
                dto.PersonalEmail, dto.Phone, dto.DateOfBirth, dto.AddressLine1, dto.AddressLine2, dto.City, dto.Region,
                dto.PostalCode, dto.AddressCountryCode, dto.NationalId,
                (dto.EmergencyContacts ?? []).Select(c => new EmergencyContactDetails(c.Name, c.Relationship, c.Phone)).ToList());

    internal static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
