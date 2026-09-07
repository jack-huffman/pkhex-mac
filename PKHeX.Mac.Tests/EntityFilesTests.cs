using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// Folder import and export used to live inside the window's view model where nothing
/// exercised them. They move real files around, so they get real files here.
/// </summary>
public sealed class EntityFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pkhex-entities-" + Guid.NewGuid().ToString("N"));

    public EntityFilesTests() => Directory.CreateDirectory(_dir);

    private static PKM Make(SaveFile sav, Species species, string nickname = "")
    {
        var pk = Fixtures.Legal(sav, species);
        if (nickname.Length > 0)
        {
            pk.Nickname = nickname;
            pk.IsNicknamed = true;
            pk.RefreshChecksum();
        }
        return pk;
    }

    [Theory]
    [InlineData("main.pk9", true)]
    [InlineData("Pikachu.PK5", true)]
    [InlineData("gift.pb7", true)]
    [InlineData("secret.ek9", true)]
    [InlineData("main", false)]
    [InlineData("notes.txt", false)]
    [InlineData("save.sav", false)]
    public void EntityExtensionsAreRecognised(string name, bool expected)
        => Assert.Equal(expected, EntityFiles.HasEntityExtension(name));

    [Fact]
    public void WriteThenReadRoundTrips()
    {
        var sav = new SAV5B2W2();
        var pk = Make(sav, Species.Pikachu, "Sparky");
        var path = Path.Combine(_dir, pk.FileName);

        Assert.True(EntityFiles.Write(pk, path, out var error), error);
        var read = EntityFiles.Read(path, sav, out error);

        Assert.NotNull(read);
        Assert.Equal(string.Empty, error);
        Assert.Equal((ushort)Species.Pikachu, read!.Species);
        Assert.Equal("Sparky", read.Nickname);
    }

    [Fact]
    public void ReadConvertsIntoTheSaveFormat()
    {
        var older = new SAV5B2W2();
        var newer = new SAV9SV();
        var path = Path.Combine(_dir, "pika.pk5");
        Assert.True(EntityFiles.Write(Make(older, Species.Pikachu), path, out _));

        var read = EntityFiles.Read(path, newer, out var error);
        Assert.NotNull(read);
        Assert.IsType<PK9>(read);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void FilesThatAreNotPokemonAreRefusedWithAReason()
    {
        var path = Path.Combine(_dir, "junk.pk9");
        File.WriteAllBytes(path, new byte[200]);
        Assert.Null(EntityFiles.Read(path, new SAV9SV(), out var error));
        Assert.NotEmpty(error);

        var big = Path.Combine(_dir, "big.pk9");
        File.WriteAllBytes(big, new byte[8192]);
        Assert.Null(EntityFiles.Read(big, new SAV9SV(), out error));
        Assert.Contains("size", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DumpingABoxNeverOverwritesAndLoadingRefillsIt()
    {
        var sav = new SAV5B2W2();
        // Two identical Pokémon collide on file name and must both survive.
        sav.SetBoxSlotAtIndex(Make(sav, Species.Eevee), 0, 0);
        sav.SetBoxSlotAtIndex(Make(sav, Species.Eevee), 0, 1);
        sav.SetBoxSlotAtIndex(Make(sav, Species.Pikachu), 0, 5);

        Assert.Equal(3, EntityFiles.DumpBox(sav, 0, _dir));
        Assert.Equal(3, Directory.GetFiles(_dir).Length);

        // Load them into an empty box of a fresh save; the pre-existing occupant is skipped over.
        var target = new SAV5B2W2();
        target.SetBoxSlotAtIndex(Make(target, Species.Bulbasaur), 1, 0);
        var result = EntityFiles.LoadFolder(target, 1, _dir);

        Assert.Equal(3, result.Loaded);
        Assert.Equal(0, result.Skipped);
        Assert.Equal((ushort)Species.Bulbasaur, target.GetBoxSlotAtIndex(1, 0).Species);
        Assert.NotEqual(0, target.GetBoxSlotAtIndex(1, 1).Species);
        Assert.NotEqual(0, target.GetBoxSlotAtIndex(1, 3).Species);
        Assert.Equal(0, target.GetBoxSlotAtIndex(1, 4).Species);
    }

    [Fact]
    public void LoadingSkipsFilesItCannotRead()
    {
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "not a pokemon");
        var result = EntityFiles.LoadFolder(new SAV5B2W2(), 0, _dir);
        Assert.Equal(0, result.Loaded);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void LoadingStopsWhenTheBoxIsFull()
    {
        var sav = new SAV5B2W2();
        for (int i = 0; i < sav.BoxSlotCount; i++)
            sav.SetBoxSlotAtIndex(Make(sav, Species.Eevee), 0, i);
        Assert.True(EntityFiles.Write(Make(sav, Species.Pikachu), Path.Combine(_dir, "pika.pk5"), out _));

        var result = EntityFiles.LoadFolder(sav, 0, _dir);
        Assert.Equal(0, result.Loaded);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
