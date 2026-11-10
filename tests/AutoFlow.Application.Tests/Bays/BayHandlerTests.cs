using AutoFlow.Application.Bays;
using AutoFlow.Application.Common;
using AutoFlow.Application.Tests.Fakes;
using AutoFlow.Domain.Bays;
using AutoFlow.Domain.Common;

namespace AutoFlow.Application.Tests.Bays;

public class BayHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly CreateBayHandler _create;
    private readonly RenameBayHandler _rename;
    private readonly TakeBayOutOfServiceHandler _takeOutOfService;
    private readonly ReturnBayToServiceHandler _returnToService;
    private readonly GetBayHandler _get;
    private readonly ListBaysHandler _list;

    public BayHandlerTests()
    {
        _create = new CreateBayHandler(_store, _store, new FixedTimeProvider());
        _rename = new RenameBayHandler(_store, _store);
        _takeOutOfService = new TakeBayOutOfServiceHandler(_store, _store);
        _returnToService = new ReturnBayToServiceHandler(_store, _store);
        _get = new GetBayHandler(_store);
        _list = new ListBaysHandler(_store);
    }

    [Fact]
    public async Task Create_adds_a_bay_in_service_with_the_trimmed_name()
    {
        var result = await _create.HandleAsync(new CreateBayCommand("  Lift 1 "));

        Assert.True(result.IsSuccess);
        Assert.Equal("Lift 1", result.Value.Name);
        Assert.True(result.Value.IsInService);
        Assert.Equal(FixedTimeProvider.Default, result.Value.CreatedAt);
        Assert.Equal(result.Value.Id, Assert.Single(_store.Bays).Id);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Create_with_a_name_in_use_returns_a_conflict_and_saves_nothing()
    {
        var existing = _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));
        existing.TakeOutOfService();

        var result = await _create.HandleAsync(new CreateBayCommand("Lift 1 "));

        Assert.True(result.IsFailure);
        Assert.Equal("bay.duplicate_name", result.Error.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Single(_store.Bays);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Create_with_a_blank_name_throws_and_saves_nothing()
    {
        await Assert.ThrowsAsync<DomainException>(() => _create.HandleAsync(new CreateBayCommand("  ")));

        Assert.Empty(_store.Bays);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Rename_changes_the_name()
    {
        var bay = _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));

        var result = await _rename.HandleAsync(new RenameBayCommand(bay.Id, "Alignment"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Alignment", result.Value.Name);
        Assert.Equal("Alignment", bay.Name);
        Assert.Equal(1, _store.SaveCount);
    }

    [Fact]
    public async Task Rename_to_the_current_name_is_allowed()
    {
        var bay = _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));

        var result = await _rename.HandleAsync(new RenameBayCommand(bay.Id, " Lift 1"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Lift 1", bay.Name);
    }

    [Fact]
    public async Task Rename_to_another_bays_name_returns_a_conflict_and_keeps_the_name()
    {
        var bay = _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));
        _store.Seed(Bay.Create("Lift 2", FixedTimeProvider.Default));

        var result = await _rename.HandleAsync(new RenameBayCommand(bay.Id, "Lift 2"));

        Assert.Equal("bay.duplicate_name", result.Error.Code);
        Assert.Equal("Lift 1", bay.Name);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Rename_to_a_blank_name_throws()
    {
        var bay = _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));

        await Assert.ThrowsAsync<DomainException>(() => _rename.HandleAsync(new RenameBayCommand(bay.Id, " ")));

        Assert.Equal("Lift 1", bay.Name);
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Take_out_of_service_and_return_to_service_toggle_the_bay()
    {
        var bay = _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));

        var outOfService = await _takeOutOfService.HandleAsync(new TakeBayOutOfServiceCommand(bay.Id));
        Assert.False(outOfService.Value.IsInService);
        Assert.False(bay.IsInService);

        var backInService = await _returnToService.HandleAsync(new ReturnBayToServiceCommand(bay.Id));
        Assert.True(backInService.Value.IsInService);
        Assert.True(bay.IsInService);
        Assert.Equal(2, _store.SaveCount);
    }

    [Fact]
    public async Task Taking_a_bay_out_of_service_twice_throws()
    {
        var bay = _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));
        bay.TakeOutOfService();

        await Assert.ThrowsAsync<DomainException>(() =>
            _takeOutOfService.HandleAsync(new TakeBayOutOfServiceCommand(bay.Id)));

        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Every_command_and_get_return_not_found_for_an_unknown_bay()
    {
        var unknown = Guid.NewGuid();

        var results = new[]
        {
            await _rename.HandleAsync(new RenameBayCommand(unknown, "Lift 9")),
            await _takeOutOfService.HandleAsync(new TakeBayOutOfServiceCommand(unknown)),
            await _returnToService.HandleAsync(new ReturnBayToServiceCommand(unknown)),
            await _get.HandleAsync(new GetBayQuery(unknown)),
        };

        Assert.All(results, result =>
        {
            Assert.Equal("bay.not_found", result.Error.Code);
            Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        });
        Assert.Equal(0, _store.SaveCount);
    }

    [Fact]
    public async Task Get_returns_the_bay()
    {
        var bay = _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));

        var result = await _get.HandleAsync(new GetBayQuery(bay.Id));

        Assert.Equal(BayDetails.From(bay), result.Value);
    }

    [Fact]
    public async Task List_returns_all_bays_by_name_including_those_out_of_service()
    {
        _store.Seed(Bay.Create("Wash bay", FixedTimeProvider.Default));
        _store.Seed(Bay.Create("Alignment", FixedTimeProvider.Default)).TakeOutOfService();
        _store.Seed(Bay.Create("Lift 1", FixedTimeProvider.Default));

        var bays = await _list.HandleAsync(new ListBaysQuery());

        Assert.Equal("Alignment, Lift 1, Wash bay", string.Join(", ", bays.Select(b => b.Name)));
        Assert.False(bays[0].IsInService);
    }
}
