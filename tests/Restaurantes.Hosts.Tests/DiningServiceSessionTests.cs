using System.Reflection;
using System.Security.Claims;
using Restaurantes.Dining.Application;
using Restaurantes.Dining.Contracts.Requests;
using Restaurantes.Dining.Domain;
using Xunit;

namespace Restaurantes.Hosts.Tests;

public sealed class DiningServiceSessionTests
{
    private static readonly Guid RestaurantId = Guid.NewGuid();
    private static readonly Guid TableId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task OpenSession_returns_not_found_for_missing_or_deleted_table()
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();

        DiningResult missing = await service.OpenSession(
            TableId,
            new OpenSessionRequest(),
            Principal(UserId),
            CancellationToken.None
        );

        store.Table = Table(deleted: true);
        DiningResult deleted = await service.OpenSession(
            TableId,
            new OpenSessionRequest(),
            Principal(UserId),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.NotFound, missing.Outcome);
        Assert.Equal(DiningOutcome.NotFound, deleted.Outcome);
    }

    [Fact]
    public async Task OpenSession_requires_permission_active_table_and_user_id()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        store.Table = Table();
        auth.AllowedPermissions.Clear();

        DiningResult forbidden = await service.OpenSession(
            TableId,
            new OpenSessionRequest(),
            Principal(UserId),
            CancellationToken.None
        );

        auth.AllowedPermissions.Add(DiningPermission.OrdersCreate);
        store.Table.IsActive = false;
        DiningResult inactive = await service.OpenSession(
            TableId,
            new OpenSessionRequest(),
            Principal(UserId),
            CancellationToken.None
        );

        store.Table.IsActive = true;
        DiningResult missingUser = await service.OpenSession(
            TableId,
            new OpenSessionRequest(),
            Principal(null),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Forbidden, forbidden.Outcome);
        Assert.Equal(DiningOutcome.Conflict, inactive.Outcome);
        Assert.Equal(DiningOutcome.Forbidden, missingUser.Outcome);
    }

    [Fact]
    public async Task OpenSession_reports_closed_cash_register()
    {
        (DiningService service, DiningSessionStoreProxy store, _, DiningCashRegisterProxy cash) =
            CreateService();
        store.Table = Table();
        cash.Failure = new DiningCashRegisterClosedException("Caja cerrada");

        DiningResult result = await service.OpenSession(
            TableId,
            new OpenSessionRequest(),
            Principal(UserId),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Conflict, result.Outcome);
        Assert.Equal(
            "Caja cerrada",
            result.Value!.GetType().GetProperty("detail")!.GetValue(result.Value)
        );
        Assert.Empty(store.AddedEntities);
    }

    [Fact]
    public async Task OpenSession_creates_session_and_commits_transaction()
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();
        store.Table = Table(requestGuestCount: true);

        DiningResult result = await service.OpenSession(
            TableId,
            new OpenSessionRequest { Source = "Pos" },
            Principal(UserId),
            CancellationToken.None
        );

        DiningSession session = Assert.IsType<DiningSession>(Assert.Single(store.AddedEntities));
        Assert.Equal(DiningOutcome.Created, result.Outcome);
        Assert.Equal($"/api/dining/sessions/{session.Id}", result.Location);
        Assert.Equal(RestaurantId, session.RestaurantId);
        Assert.Equal(TableId, session.TableId);
        Assert.Equal(UserId, session.OpenedByUserId);
        Assert.True(session.RequestGuestCount);
        Assert.Equal("Pos", session.Source);
        Assert.True(store.Transaction.IsCommitted);
    }

    [Fact]
    public async Task OpenSession_returns_conflict_when_table_already_has_session()
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();
        store.Table = Table();
        store.ThrowDuplicateOnSave = true;

        DiningResult result = await service.OpenSession(
            TableId,
            new OpenSessionRequest(),
            Principal(UserId),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task ActiveSession_and_GetSession_enforce_visibility()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        store.Session = Session();

        DiningResult active = await service.ActiveSession(
            TableId,
            Principal(UserId),
            CancellationToken.None
        );
        DiningResult found = await service.GetSession(
            store.Session.Id,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, active.Outcome);
        Assert.Equal(DiningOutcome.Ok, found.Outcome);

        auth.AllowedPermissions.Clear();
        DiningResult activeForbidden = await service.ActiveSession(
            TableId,
            Principal(UserId),
            CancellationToken.None
        );
        DiningResult foundForbidden = await service.GetSession(
            store.Session.Id,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Forbidden, activeForbidden.Outcome);
        Assert.Equal(DiningOutcome.Forbidden, foundForbidden.Outcome);

        store.Session = null;
        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.ActiveSession(TableId, Principal(UserId), CancellationToken.None)
            ).Outcome
        );
        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.GetSession(Guid.NewGuid(), Principal(UserId), CancellationToken.None)
            ).Outcome
        );
    }

    [Fact]
    public async Task ValidateSession_checks_open_session_guest_count_mode_and_qr_token()
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();
        DiningRequest request = new(Principal(UserId), "secret-token", null);
        DiningSession session = Session(requestGuestCount: true);

        DiningResult missing = await service.ValidateSession(
            session.Id,
            RestaurantId,
            TableId,
            "CustomerQr",
            "DineIn",
            request,
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Conflict, missing.Outcome);

        store.Session = session;
        DiningResult guestCountRequired = await service.ValidateSession(
            session.Id,
            RestaurantId,
            TableId,
            "CustomerQr",
            "DineIn",
            request,
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Conflict, guestCountRequired.Outcome);

        session.GuestCount = 2;
        DiningResult wrongMode = await service.ValidateSession(
            session.Id,
            RestaurantId,
            TableId,
            "CustomerQr",
            "Takeaway",
            request,
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Conflict, wrongMode.Outcome);

        session.Source = "CustomerQr";
        session.CustomerAccessToken = "secret-token";
        DiningResult validQr = await service.ValidateSession(
            session.Id,
            RestaurantId,
            TableId,
            "CustomerQr",
            "DineIn",
            request with
            {
                CustomerSessionToken = " secret-token ",
            },
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.NoContent, validQr.Outcome);
        Assert.Equal(2, validQr.GuestCount);
    }

    [Theory]
    [InlineData("Other", "DineIn", DiningOutcome.NoContent)]
    [InlineData("CustomerQr", "Bar", DiningOutcome.Conflict)]
    [InlineData("CustomerQr", "DineIn", DiningOutcome.Conflict)]
    [InlineData("Other", "Takeaway", DiningOutcome.Conflict)]
    public async Task ValidateSession_accepts_only_valid_source_and_service_mode(
        string source,
        string mode,
        DiningOutcome expected
    )
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();
        store.Session = Session();
        store.Session.Source = "Comandero";

        DiningResult result = await service.ValidateSession(
            store.Session.Id,
            RestaurantId,
            TableId,
            source,
            mode,
            new DiningRequest(Principal(UserId), null, null),
            CancellationToken.None
        );

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task CloseSession_requires_permission_and_rejects_closed_or_empty_sessions()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.CloseSession(
                    Guid.NewGuid(),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.Session = Session();
        auth.AllowedPermissions.Clear();
        Assert.Equal(
            DiningOutcome.Forbidden,
            (
                await service.CloseSession(
                    store.Session.Id,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        auth.AllowedPermissions.Add(DiningPermission.TablesRelease);
        store.Session.Status = "Closed";
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.CloseSession(
                    store.Session.Id,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.Session.Status = "Open";
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.CloseSession(
                    store.Session.Id,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
    }

    [Fact]
    public async Task CloseSession_rejects_unpaid_and_undelivered_orders_then_closes_paid_session()
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();
        store.Session = Session();
        store.Session.Orders = [Order(amount: 20m, amountPaid: 0m, orderStatus: "Delivered")];

        DiningResult unpaid = await service.CloseSession(
            store.Session.Id,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Conflict, unpaid.Outcome);

        store.Session.Orders[0].AmountPaid = 20m;
        store.Session.Orders[0].OrderStatus = "Submitted";
        DiningResult unfinished = await service.CloseSession(
            store.Session.Id,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Conflict, unfinished.Outcome);

        store.Session.Orders[0].OrderStatus = "Delivered";
        DiningResult closed = await service.CloseSession(
            store.Session.Id,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, closed.Outcome);
        Assert.Equal("Closed", store.Session.Status);
        Assert.True(store.SavedChanges);
    }

    [Fact]
    public async Task CloseSession_allows_cancelled_orders_without_payment()
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();
        store.Session = Session();
        store.Session.Orders = [Order(amount: 20m, amountPaid: 0m, orderStatus: "Cancelled")];

        DiningResult result = await service.CloseSession(
            store.Session.Id,
            Principal(UserId),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Ok, result.Outcome);
        Assert.Equal("Closed", store.Session.Status);
    }

    [Fact]
    public async Task CloseSession_allows_paid_commander_order_with_paid_release_permission()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService(DiningPermission.TablesReleasePaid);
        DiningSession session = Session();
        session.Orders =
        [
            Order(
                amount: 20m,
                amountPaid: 20m,
                orderStatus: "Delivered",
                source: "Comandero",
                createdBy: UserId
            ),
        ];
        store.Session = session;
        auth.AllowedPermissions.Remove(DiningPermission.TablesRelease);

        DiningResult result = await service.CloseSession(
            session.Id,
            Principal(UserId),
            CancellationToken.None
        );

        Assert.Equal(DiningOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task CancelSession_requires_user_access_and_empty_open_session()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.CancelSession(
                    Guid.NewGuid(),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.Session = Session(openedBy: UserId);
        Assert.Equal(
            DiningOutcome.Forbidden,
            (
                await service.CancelSession(
                    store.Session.Id,
                    Principal(null),
                    CancellationToken.None
                )
            ).Outcome
        );

        auth.AllowedPermissions.Clear();
        Assert.Equal(
            DiningOutcome.Forbidden,
            (
                await service.CancelSession(
                    store.Session.Id,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        auth.AllowedPermissions.Add(DiningPermission.OrdersCreate);
        store.Session.Status = "Closed";
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.CancelSession(
                    store.Session.Id,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.Session.Status = "Open";
        store.Session.Orders = [Order()];
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.CancelSession(
                    store.Session.Id,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.Session.Orders.Clear();
        DiningResult cancelled = await service.CancelSession(
            store.Session.Id,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, cancelled.Outcome);
        Assert.Equal("Cancelled", store.Session.Status);
        Assert.Equal(UserId, store.Session.CancelledByUserId);
        Assert.True(store.SavedChanges);
    }

    [Fact]
    public async Task Zone_listing_and_creation_validate_access_and_request_guest_count()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        DiningZone legacyZone = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = RestaurantId,
            RequestGuestCount = null,
        };
        store.Zones.Add(legacyZone);
        auth.AllowedPermissions.Clear();

        Assert.Equal(
            DiningOutcome.Forbidden,
            (
                await service.ListZones(RestaurantId, Principal(UserId), CancellationToken.None)
            ).Outcome
        );
        Assert.Equal(
            DiningOutcome.BadRequest,
            (
                await service.CreateZone(
                    Guid.Empty,
                    new SaveZoneRequest { Name = "Bar" },
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(
            DiningOutcome.Forbidden,
            (
                await service.CreateZone(
                    RestaurantId,
                    new SaveZoneRequest { Name = "Bar" },
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        auth.AllowedPermissions.Add(DiningPermission.TablesRead);
        DiningResult listed = await service.ListZones(
            RestaurantId,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(false, legacyZone.RequestGuestCount);
        Assert.Single(Assert.IsType<List<DiningZone>>(listed.Value));

        auth.AllowedPermissions.Add(DiningPermission.TablesManage);
        DiningResult invalid = await service.CreateZone(
            RestaurantId,
            new SaveZoneRequest { Name = " ", SortOrder = -1 },
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.BadRequest, invalid.Outcome);
        Assert.False(store.SavedChanges);

        DiningResult created = await service.CreateZone(
            RestaurantId,
            new SaveZoneRequest
            {
                Name = "  Terraza  ",
                SortOrder = 2,
                RequestGuestCount = true,
            },
            Principal(UserId),
            CancellationToken.None
        );
        DiningZone createdZone = Assert.IsType<DiningZone>(created.Value);
        Assert.Equal(DiningOutcome.Created, created.Outcome);
        Assert.Equal("Terraza", createdZone.Name);
        Assert.Equal(2, createdZone.SortOrder);
        Assert.True(createdZone.RequestGuestCount);
        Assert.Equal(
            $"/api/dining/restaurants/{RestaurantId}/zones/{createdZone.Id}",
            created.Location
        );
    }

    [Fact]
    public async Task Zone_update_returns_not_found_and_maps_duplicate_and_concurrency_conflicts()
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();
        DiningZone zone = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = RestaurantId,
            Name = "Interior",
        };
        store.Zone = zone;

        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.UpdateZone(
                    RestaurantId,
                    Guid.NewGuid(),
                    new SaveZoneRequest { Name = "Bar" },
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.ThrowSaveFailure = DiningStoreFailure.Duplicate;
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.UpdateZone(
                    RestaurantId,
                    zone.Id,
                    new SaveZoneRequest { Name = "Bar" },
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        store.ThrowSaveFailure = DiningStoreFailure.Concurrency;
        DiningResult conflict = await service.UpdateZone(
            RestaurantId,
            zone.Id,
            new SaveZoneRequest { Name = "Bar" },
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Conflict, conflict.Outcome);

        store.ThrowSaveFailure = null;
        DiningResult updated = await service.UpdateZone(
            RestaurantId,
            zone.Id,
            new SaveZoneRequest
            {
                Name = "  Terraza ",
                SortOrder = 4,
                RequestGuestCount = true,
            },
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, updated.Outcome);
        Assert.Equal("Terraza", zone.Name);
        Assert.Equal(4, zone.SortOrder);
    }

    [Fact]
    public async Task Table_creation_checks_zone_and_maps_duplicate_and_foreign_key_conflicts()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        Guid zoneId = Guid.NewGuid();
        store.Zone = new DiningZone
        {
            Id = zoneId,
            RestaurantId = RestaurantId,
            Name = "Comedor",
        };
        auth.AllowedPermissions.Remove(DiningPermission.TablesManage);
        CreateTableRequest request = new()
        {
            ZoneId = zoneId,
            Code = " t-1 ",
            Label = " Mesa 1 ",
        };

        Assert.Equal(
            DiningOutcome.BadRequest,
            (
                await service.CreateTable(
                    Guid.Empty,
                    request,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(
            DiningOutcome.Forbidden,
            (
                await service.CreateTable(
                    RestaurantId,
                    request,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        auth.AllowedPermissions.Add(DiningPermission.TablesManage);
        store.Zone = null;
        Assert.Equal(
            DiningOutcome.BadRequest,
            (
                await service.CreateTable(
                    RestaurantId,
                    request,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.Zone = new DiningZone
        {
            Id = zoneId,
            RestaurantId = RestaurantId,
            Name = "Comedor",
        };
        DiningResult created = await service.CreateTable(
            RestaurantId,
            request,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal("T-1", Property(created.Value!, "Code"));
        Assert.Equal("Mesa 1", Property(created.Value!, "Label"));
        Assert.Equal(DiningOutcome.Created, created.Outcome);
        string qrCode = Assert.IsType<string>(Property(created.Value!, "QrCode"));
        Assert.Equal(32, qrCode.Length);
        Assert.Equal($"/api/dining/tables/{Property(created.Value!, "Id")}", created.Location);

        store.ThrowSaveFailure = DiningStoreFailure.Duplicate;
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.CreateTable(
                    RestaurantId,
                    request,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        store.ThrowSaveFailure = DiningStoreFailure.ForeignKey;
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.CreateTable(
                    RestaurantId,
                    request,
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
    }

    [Fact]
    public async Task Table_update_and_list_cover_occupied_and_active_session_responses()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        DiningZone zone = new()
        {
            Id = Guid.NewGuid(),
            RestaurantId = RestaurantId,
            Name = "Terraza",
        };
        RestaurantTable table = Table();
        table.Zone = zone;
        table.ZoneId = zone.Id;
        store.Zone = zone;
        store.Table = table;

        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.UpdateTable(
                    Guid.NewGuid(),
                    new UpdateTableRequest(),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.HasOpenSession = true;
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.UpdateTable(
                    TableId,
                    new UpdateTableRequest { ZoneId = zone.Id, IsActive = false },
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.HasOpenSession = false;
        store.ThrowSaveFailure = DiningStoreFailure.ForeignKey;
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.UpdateTable(
                    TableId,
                    new UpdateTableRequest { ZoneId = zone.Id, Label = "Nueva" },
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.ThrowSaveFailure = null;
        store.HasOpenSession = true;
        DiningSession session = Session();
        store.RequiredActiveSession = session;
        DiningResult updated = await service.UpdateTable(
            TableId,
            new UpdateTableRequest
            {
                ZoneId = zone.Id,
                Label = "  Mesa 1  ",
                IsActive = true,
            },
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, updated.Outcome);
        Assert.Equal("Mesa 1", table.Label);
        Assert.Equal("Occupied", Property(updated.Value!, "status"));
        Assert.Equal(session.Id, Property(updated.Value!, "activeSessionId"));

        store.Tables.Add(table);
        store.ActiveSessions[table.Id] = session;
        DiningResult listed = await service.ListTables(
            RestaurantId,
            Principal(UserId),
            CancellationToken.None
        );
        object listedTable = Assert.Single(
            Assert.IsType<IEnumerable<object>>(listed.Value!, exactMatch: false)
        );
        Assert.Equal("Occupied", Property(listedTable, "status"));
        Assert.Equal("Terraza", Property(listedTable, "zoneName"));
        auth.AllowedPermissions.Remove(DiningPermission.TablesRead);
        Assert.Equal(
            DiningOutcome.Forbidden,
            (
                await service.ListTables(RestaurantId, Principal(null), CancellationToken.None)
            ).Outcome
        );
    }

    [Fact]
    public async Task Table_qr_rotation_and_deletion_enforce_permissions_and_history_guards()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        RestaurantTable table = Table();
        table.QrCode = "OLD";
        store.Table = table;

        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.RotateQr(Guid.NewGuid(), Principal(UserId), CancellationToken.None)
            ).Outcome
        );
        auth.AllowedPermissions.Remove(DiningPermission.TablesManage);
        Assert.Equal(
            DiningOutcome.Forbidden,
            (await service.RotateQr(TableId, Principal(UserId), CancellationToken.None)).Outcome
        );
        Assert.Equal(
            DiningOutcome.Forbidden,
            (await service.DeleteTable(TableId, Principal(UserId), CancellationToken.None)).Outcome
        );

        auth.AllowedPermissions.Add(DiningPermission.TablesManage);
        DiningResult rotated = await service.RotateQr(
            TableId,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, rotated.Outcome);
        Assert.NotEqual("OLD", table.QrCode);
        Assert.Equal(32, table.QrCode.Length);

        store.HasOpenSession = true;
        Assert.Equal(
            DiningOutcome.Conflict,
            (await service.DeleteTable(TableId, Principal(UserId), CancellationToken.None)).Outcome
        );
        store.HasOpenSession = false;
        store.HasSalesForTable = true;
        Assert.Equal(
            DiningOutcome.Conflict,
            (await service.DeleteTable(TableId, Principal(UserId), CancellationToken.None)).Outcome
        );
        store.HasSalesForTable = false;
        DiningResult deleted = await service.DeleteTable(
            TableId,
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.NoContent, deleted.Outcome);
        Assert.NotNull(table.DeletedAtUtc);
        Assert.False(table.IsActive);
        Assert.True(store.Transaction.IsCommitted);
    }

    private static object? Property(object instance, string name) =>
        instance.GetType().GetProperty(name)!.GetValue(instance);

    [Fact]
    public async Task Guest_count_requires_staff_or_valid_customer_token_and_is_idempotent()
    {
        (DiningService service, DiningSessionStoreProxy store, _, _) = CreateService();
        ClaimsPrincipal anonymousPrincipal = new(new ClaimsIdentity());
        DiningRequest anonymous = new(anonymousPrincipal, null, null);

        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.SetGuestCount(
                    Guid.NewGuid(),
                    new SetGuestCountRequest(2),
                    anonymous,
                    CancellationToken.None
                )
            ).Outcome
        );
        DiningSession session = Session();
        store.Session = session;
        Assert.Equal(
            DiningOutcome.Unauthorized,
            (
                await service.SetGuestCount(
                    session.Id,
                    new SetGuestCountRequest(2),
                    anonymous,
                    CancellationToken.None
                )
            ).Outcome
        );

        DiningRequest staff = new(Principal(UserId), null, null);
        session.Status = "Closed";
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.SetGuestCount(
                    session.Id,
                    new SetGuestCountRequest(2),
                    staff,
                    CancellationToken.None
                )
            ).Outcome
        );
        session.Status = "Open";
        Assert.Equal(
            DiningOutcome.BadRequest,
            (
                await service.SetGuestCount(
                    session.Id,
                    new SetGuestCountRequest(0),
                    staff,
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(
            DiningOutcome.BadRequest,
            (
                await service.SetGuestCount(
                    session.Id,
                    new SetGuestCountRequest(1000),
                    staff,
                    CancellationToken.None
                )
            ).Outcome
        );

        Assert.Equal(
            DiningOutcome.Ok,
            (
                await service.SetGuestCount(
                    session.Id,
                    new SetGuestCountRequest(4),
                    staff,
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(4, session.GuestCount);
        Assert.Equal(2, session.Version);
        Assert.Equal(
            DiningOutcome.Ok,
            (
                await service.SetGuestCount(
                    session.Id,
                    new SetGuestCountRequest(4),
                    staff,
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.SetGuestCount(
                    session.Id,
                    new SetGuestCountRequest(5),
                    staff,
                    CancellationToken.None
                )
            ).Outcome
        );

        session.GuestCount = null;
        session.Source = "CustomerQr";
        session.CustomerAccessToken = "qr-token";
        DiningRequest customer = new(anonymousPrincipal, " qr-token ", null);
        Assert.Equal(
            DiningOutcome.Ok,
            (
                await service.SetGuestCount(
                    session.Id,
                    new SetGuestCountRequest(2),
                    customer,
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(2, session.GuestCount);
    }

    [Fact]
    public async Task Session_discount_checks_state_permission_bounds_and_concurrency()
    {
        (DiningService service, DiningSessionStoreProxy store, DiningAuthorizationProxy auth, _) =
            CreateService();
        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.SetSessionDiscount(
                    Guid.NewGuid(),
                    new SetSessionDiscountRequest(10),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );

        DiningSession session = Session();
        store.Session = session;
        auth.AllowedPermissions.Remove(DiningPermission.DiscountsApply);
        Assert.Equal(
            DiningOutcome.Forbidden,
            (
                await service.SetSessionDiscount(
                    session.Id,
                    new SetSessionDiscountRequest(10),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        auth.AllowedPermissions.Add(DiningPermission.DiscountsApply);

        session.Status = "Closed";
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.SetSessionDiscount(
                    session.Id,
                    new SetSessionDiscountRequest(10),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        session.Status = "Open";
        session.CheckoutIdempotencyKey = Guid.NewGuid();
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.SetSessionDiscount(
                    session.Id,
                    new SetSessionDiscountRequest(10),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        session.CheckoutIdempotencyKey = null;
        session.Orders = [Order(amountPaid: 1)];
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.SetSessionDiscount(
                    session.Id,
                    new SetSessionDiscountRequest(10),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        session.Orders.Clear();

        Assert.Equal(
            DiningOutcome.BadRequest,
            (
                await service.SetSessionDiscount(
                    session.Id,
                    new SetSessionDiscountRequest(-1),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(
            DiningOutcome.BadRequest,
            (
                await service.SetSessionDiscount(
                    session.Id,
                    new SetSessionDiscountRequest(100),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(
            DiningOutcome.Ok,
            (
                await service.SetSessionDiscount(
                    session.Id,
                    new SetSessionDiscountRequest(0),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
        Assert.Equal(0, store.SaveCalls);

        DiningResult updated = await service.SetSessionDiscount(
            session.Id,
            new SetSessionDiscountRequest(15),
            Principal(UserId),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, updated.Outcome);
        Assert.Equal(15, session.DiscountPercentage);
        Assert.Equal(2, session.Version);

        store.ThrowSaveFailure = DiningStoreFailure.Concurrency;
        Assert.Equal(
            DiningOutcome.Conflict,
            (
                await service.SetSessionDiscount(
                    session.Id,
                    new SetSessionDiscountRequest(20),
                    Principal(UserId),
                    CancellationToken.None
                )
            ).Outcome
        );
    }

    [Fact]
    public async Task Preview_qr_hides_other_sessions_and_returns_only_the_customer_token_holder_account()
    {
        (DiningService service, DiningSessionStoreProxy store, _, DiningCashRegisterProxy cash) =
            CreateService();
        RestaurantTable table = Table();
        table.QrCode = "ABC123";
        table.Zone.Name = "Terraza";
        store.Table = table;

        Assert.Equal(
            DiningOutcome.NotFound,
            (
                await service.PreviewQr(
                    " missing ",
                    new DiningRequest(Principal(null), null, null),
                    CancellationToken.None
                )
            ).Outcome
        );

        cash.Failure = new DiningCashRegisterClosedException("Caja cerrada");
        DiningResult anonymous = await service.PreviewQr(
            " abc123 ",
            new DiningRequest(Principal(null), null, null),
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Ok, anonymous.Outcome);
        Assert.False((bool)Property(anonymous.Value!, "isRestaurantOpen")!);
        Assert.Null(Property(anonymous.Value!, "session"));

        DiningSession customerSession = Session();
        customerSession.Source = "CustomerQr";
        customerSession.CustomerAccessToken = "private-token";
        store.Session = customerSession;
        DiningResult wrongToken = await service.PreviewQr(
            "ABC123",
            new DiningRequest(Principal(null), "wrong", null),
            CancellationToken.None
        );
        Assert.Null(Property(wrongToken.Value!, "session"));
        DiningResult owner = await service.PreviewQr(
            "ABC123",
            new DiningRequest(Principal(null), " private-token ", null),
            CancellationToken.None
        );
        Assert.NotNull(Property(owner.Value!, "session"));
        Assert.Equal("private-token", Property(owner.Value!, "customerAccessToken"));
    }

    [Fact]
    public async Task Open_qr_session_checks_table_and_session_state_and_handles_duplicate_race()
    {
        (DiningService service, DiningSessionStoreProxy store, _, DiningCashRegisterProxy cash) =
            CreateService();
        DiningRequest anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()), null, null);

        Assert.Equal(
            DiningOutcome.NotFound,
            (await service.OpenQrSession("ABC123", null, anonymous, CancellationToken.None)).Outcome
        );
        RestaurantTable table = Table();
        table.QrCode = "ABC123";
        table.Zone.RequestGuestCount = true;
        store.Table = table;

        table.DeletedAtUtc = DateTime.UtcNow;
        Assert.Equal(
            DiningOutcome.NotFound,
            (await service.OpenQrSession("ABC123", 2, anonymous, CancellationToken.None)).Outcome
        );
        table.DeletedAtUtc = null;
        table.IsActive = false;
        Assert.Equal(
            DiningOutcome.Conflict,
            (await service.OpenQrSession("ABC123", 2, anonymous, CancellationToken.None)).Outcome
        );
        table.IsActive = true;
        cash.Failure = new DiningCashRegisterClosedException("Caja cerrada");
        Assert.Equal(
            DiningOutcome.Conflict,
            (await service.OpenQrSession("ABC123", 2, anonymous, CancellationToken.None)).Outcome
        );
        cash.Failure = null;

        store.Session = Session();
        Assert.Equal(
            DiningOutcome.Conflict,
            (await service.OpenQrSession("ABC123", 2, anonymous, CancellationToken.None)).Outcome
        );
        store.Session.Source = "CustomerQr";
        store.Session.CustomerAccessToken = "private-token";
        Assert.Equal(
            DiningOutcome.Conflict,
            (await service.OpenQrSession("ABC123", 2, anonymous, CancellationToken.None)).Outcome
        );
        Assert.Equal(
            DiningOutcome.Ok,
            (
                await service.OpenQrSession(
                    "ABC123",
                    2,
                    new DiningRequest(anonymous.User, "private-token", null),
                    CancellationToken.None
                )
            ).Outcome
        );

        store.Session = null;
        Assert.Equal(
            DiningOutcome.BadRequest,
            (await service.OpenQrSession("ABC123", null, anonymous, CancellationToken.None)).Outcome
        );
        Assert.Equal(
            DiningOutcome.BadRequest,
            (await service.OpenQrSession("ABC123", 0, anonymous, CancellationToken.None)).Outcome
        );

        DiningResult opened = await service.OpenQrSession(
            " abc123 ",
            3,
            anonymous,
            CancellationToken.None
        );
        Assert.Equal(DiningOutcome.Created, opened.Outcome);
        DiningSession openedSession = Assert.Single(store.AddedEntities.OfType<DiningSession>());
        Assert.Equal("CustomerQr", openedSession.Source);
        Assert.Equal(3, openedSession.GuestCount);
        Assert.Equal(64, openedSession.CustomerAccessToken.Length);
        Assert.True(store.Transaction.IsCommitted);

        store.ThrowSaveFailure = DiningStoreFailure.Duplicate;
        Assert.Equal(
            DiningOutcome.Conflict,
            (await service.OpenQrSession("ABC123", 4, anonymous, CancellationToken.None)).Outcome
        );
        Assert.True(store.DetachedSession);
    }

    private static (
        DiningService Service,
        DiningSessionStoreProxy Store,
        DiningAuthorizationProxy Authorization,
        DiningCashRegisterProxy CashRegister
    ) CreateService(params DiningPermission[] permissions)
    {
        IDiningStore store = DispatchProxy.Create<IDiningStore, DiningSessionStoreProxy>();
        DiningSessionStoreProxy storeProxy = (DiningSessionStoreProxy)(object)store;
        IDiningAuthorization authorization = DispatchProxy.Create<
            IDiningAuthorization,
            DiningAuthorizationProxy
        >();
        DiningAuthorizationProxy authorizationProxy = (DiningAuthorizationProxy)
            (object)authorization;
        authorizationProxy.AllowedPermissions.AddRange(
            permissions.Length == 0 ? Enum.GetValues<DiningPermission>() : permissions
        );
        IDiningCashRegister cashRegister = DispatchProxy.Create<
            IDiningCashRegister,
            DiningCashRegisterProxy
        >();
        return (
            new DiningService(store, null!, cashRegister, authorization, TimeProvider.System),
            storeProxy,
            authorizationProxy,
            (DiningCashRegisterProxy)(object)cashRegister
        );
    }

    private static ClaimsPrincipal Principal(Guid? userId)
    {
        ClaimsIdentity identity = new(
            userId is Guid id ? [new Claim(ClaimTypes.NameIdentifier, id.ToString())] : [],
            "test"
        );
        return new ClaimsPrincipal(identity);
    }

    private static RestaurantTable Table(bool deleted = false, bool requestGuestCount = false)
    {
        return new RestaurantTable
        {
            Id = TableId,
            RestaurantId = RestaurantId,
            IsActive = true,
            DeletedAtUtc = deleted ? DateTime.UtcNow : null,
            Zone = new DiningZone { RequestGuestCount = requestGuestCount },
        };
    }

    private static DiningSession Session(bool requestGuestCount = false, Guid? openedBy = null)
    {
        return new DiningSession
        {
            Id = Guid.NewGuid(),
            RestaurantId = RestaurantId,
            TableId = TableId,
            OpenedByUserId = openedBy,
            Source = "Comandero",
            RequestGuestCount = requestGuestCount,
        };
    }

    private static DiningSessionOrder Order(
        decimal amount = 0,
        decimal amountPaid = 0,
        string orderStatus = "Draft",
        string source = "Pos",
        Guid? createdBy = null
    )
    {
        return new DiningSessionOrder
        {
            OrderId = Guid.NewGuid(),
            DiningSessionId = Guid.NewGuid(),
            Amount = amount,
            AmountPaid = amountPaid,
            OrderStatus = orderStatus,
            Source = source,
            CreatedByUserId = createdBy,
        };
    }

    public class DiningSessionStoreProxy : DispatchProxy
    {
        public RestaurantTable? Table { get; set; }
        public DiningSession? Session { get; set; }
        public DiningSession? RequiredActiveSession { get; set; }
        public DiningZone? Zone { get; set; }
        public List<DiningZone> Zones { get; } = [];
        public List<RestaurantTable> Tables { get; } = [];
        public Dictionary<Guid, DiningSession> ActiveSessions { get; } = [];
        public List<object> AddedEntities { get; } = [];
        public DiningIntegrationPaymentTests.DiningTestTransaction Transaction { get; } = new();
        public bool ThrowDuplicateOnSave { get; set; }
        public DiningStoreFailure? ThrowSaveFailure { get; set; }
        public bool HasOpenSession { get; set; }
        public bool HasSalesForTable { get; set; }
        public bool DetachedSession { get; private set; }
        public bool SavedChanges { get; private set; }
        public int SaveCalls { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name switch
            {
                nameof(IDiningStore.BeginTransactionAsync) => Task.FromResult<IDiningTransaction>(
                    Transaction
                ),
                nameof(IDiningStore.ReadQrTableAsync) => Task.FromResult(
                    Table?.QrCode == (string)args![0]! ? Table : null
                ),
                nameof(IDiningStore.LockQrTableAsync) => Task.FromResult(
                    Table?.QrCode == (string)args![0]! ? Table : null
                ),
                nameof(IDiningStore.LockTableAsync) => Task.FromResult(
                    Table?.Id == (Guid)args![0]! ? Table : null
                ),
                nameof(IDiningStore.LockZoneAsync) => Task.FromResult(
                    Zone?.Id == (Guid)args![1]! ? Zone : null
                ),
                nameof(IDiningStore.FindZoneAsync) => Task.FromResult(
                    Zone?.Id == (Guid)args![1]! ? Zone : null
                ),
                nameof(IDiningStore.ListZonesAsync) => Task.FromResult(Zones),
                nameof(IDiningStore.ListTablesAsync) => Task.FromResult(Tables),
                nameof(IDiningStore.ReadActiveSessionsAsync) => Task.FromResult(ActiveSessions),
                nameof(IDiningStore.HasOpenSessionAsync) => Task.FromResult(HasOpenSession),
                nameof(IDiningStore.HasSalesForTableAsync) => Task.FromResult(HasSalesForTable),
                nameof(IDiningStore.ReadRequiredActiveSessionAsync) => Task.FromResult(
                    RequiredActiveSession!
                ),
                nameof(IDiningStore.LockSessionAsync) => Task.FromResult(Session),
                nameof(IDiningStore.LoadOrdersAsync) => Task.CompletedTask,
                nameof(IDiningStore.ReadActiveSessionWithOrdersAsync) => Task.FromResult(Session),
                nameof(IDiningStore.FindActiveSessionWithOrdersAsync) => Task.FromResult(Session),
                nameof(IDiningStore.ReadSessionWithOrdersAsync) => Task.FromResult(Session),
                nameof(IDiningStore.ReadValidatedSessionAsync) => Task.FromResult(Session),
                nameof(IDiningStore.FindSessionWithOrdersAsync) => Task.FromResult(Session),
                nameof(IDiningStore.Add) => Add(args![0]!),
                nameof(IDiningStore.Detach) => Detach(),
                nameof(IDiningStore.SaveChangesAsync) => SaveChanges(),
                _ => throw new NotSupportedException(targetMethod?.Name),
            };
        }

        private object? Add(object entity)
        {
            AddedEntities.Add(entity);
            return null;
        }

        private object? Detach()
        {
            DetachedSession = true;
            return null;
        }

        private Task SaveChanges()
        {
            SavedChanges = true;
            SaveCalls++;
            if (ThrowSaveFailure is DiningStoreFailure failure)
            {
                throw new DiningStoreException(failure, new InvalidOperationException("store"));
            }

            if (ThrowDuplicateOnSave)
            {
                throw new DiningStoreException(
                    DiningStoreFailure.Duplicate,
                    new InvalidOperationException("duplicate")
                );
            }

            return Task.CompletedTask;
        }
    }

    public class DiningAuthorizationProxy : DispatchProxy
    {
        public List<DiningPermission> AllowedPermissions { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name == nameof(IDiningAuthorization.CanAccessRestaurant)
                ? AllowedPermissions.Contains((DiningPermission)args![2]!)
                : throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public class DiningCashRegisterProxy : DispatchProxy
    {
        public Exception? Failure { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IDiningCashRegister.EnsureOpenAsync))
            {
                throw new NotSupportedException(targetMethod?.Name);
            }

            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }
    }
}
