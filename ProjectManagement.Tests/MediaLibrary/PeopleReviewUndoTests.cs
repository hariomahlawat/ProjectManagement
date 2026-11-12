using ProjectManagement.Pages.Photos.People;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// The note the Review people page keeps about the decision just made, so that the message
/// confirming it can offer Undo: who made it, when, and which faces.
/// </summary>
public sealed class PeopleReviewUndoTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Person = Guid.NewGuid();
    private static readonly Guid[] Faces = { Guid.NewGuid(), Guid.NewGuid() };

    [Fact]
    public void A_naming_is_read_back_with_its_person_and_faces()
    {
        var state = PeopleReviewUndo.ForNaming(Person, Faces, "reviewer", Now);

        var undo = PeopleReviewUndo.Read(state, "reviewer", Now.AddMinutes(2));

        Assert.NotNull(undo);
        Assert.Equal(PeopleReviewUndo.Named, undo!.Kind);
        Assert.Equal(Person, undo.PersonId);
        Assert.Equal(Faces, undo.FaceIds);
    }

    [Fact]
    public void Leaving_faces_unnamed_is_read_back_without_a_person()
    {
        var state = PeopleReviewUndo.ForLeavingUnnamed(new[] { Faces[0], Faces[0], Guid.Empty }, "reviewer", Now);

        var undo = PeopleReviewUndo.Read(state, "reviewer", Now);

        Assert.NotNull(undo);
        Assert.Equal(PeopleReviewUndo.LeftUnnamed, undo!.Kind);
        Assert.Null(undo.PersonId);
        Assert.Equal(new[] { Faces[0] }, undo.FaceIds);
    }

    [Fact]
    public void A_confirmation_can_only_undo_the_decision_it_shows()
    {
        var first = PeopleReviewUndo.ForNaming(Person, Faces, "reviewer", Now);
        var firstId = PeopleReviewUndo.Read(first, "reviewer", Now)!.Id;
        // A second decision, made in another tab, replaces the note.
        var second = PeopleReviewUndo.ForLeavingUnnamed(new[] { Guid.NewGuid() }, "reviewer", Now.AddSeconds(5));
        var secondId = PeopleReviewUndo.Read(second, "reviewer", Now.AddSeconds(5))!.Id;

        Assert.NotEqual(firstId, secondId);
        Assert.Null(PeopleReviewUndo.Read(second, firstId, "reviewer", Now.AddSeconds(10)));
        Assert.NotNull(PeopleReviewUndo.Read(second, secondId, "reviewer", Now.AddSeconds(10)));
        Assert.Null(PeopleReviewUndo.Read(second, null, "reviewer", Now.AddSeconds(10)));
    }

    [Fact]
    public void It_is_not_honoured_for_another_user()
        => Assert.Null(PeopleReviewUndo.Read(PeopleReviewUndo.ForNaming(Person, Faces, "reviewer", Now), "someone-else", Now));

    [Fact]
    public void It_is_not_honoured_after_its_lifetime()
    {
        var state = PeopleReviewUndo.ForNaming(Person, Faces, "reviewer", Now);

        Assert.NotNull(PeopleReviewUndo.Read(state, "reviewer", Now + PeopleReviewUndo.Lifetime));
        Assert.Null(PeopleReviewUndo.Read(state, "reviewer", Now + PeopleReviewUndo.Lifetime + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void No_note_is_kept_for_nothing_or_for_too_many_faces()
    {
        Assert.Null(PeopleReviewUndo.ForNaming(Person, Array.Empty<Guid>(), "reviewer", Now));
        Assert.Null(PeopleReviewUndo.ForLeavingUnnamed(new[] { Guid.Empty }, "reviewer", Now));
        Assert.Null(PeopleReviewUndo.ForNaming(Person, Faces, " ", Now));

        var many = Enumerable.Range(0, PeopleReviewUndo.MaximumFaces + 1).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Null(PeopleReviewUndo.ForNaming(Person, many, "reviewer", Now));
        Assert.NotNull(PeopleReviewUndo.ForNaming(Person, many.Take(PeopleReviewUndo.MaximumFaces).ToArray(), "reviewer", Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Id\":\"a\",\"Kind\":\"something-else\",\"PersonId\":null,\"FaceIds\":[\"7d0e3c0e-3c1a-4c5e-9a55-0f8f6c1d2b3a\"],\"UserId\":\"reviewer\",\"DecidedAtUtc\":\"2026-11-12T09:00:00+00:00\"}")]
    [InlineData("{\"Id\":\"a\",\"Kind\":\"named\",\"PersonId\":null,\"FaceIds\":[\"7d0e3c0e-3c1a-4c5e-9a55-0f8f6c1d2b3a\"],\"UserId\":\"reviewer\",\"DecidedAtUtc\":\"2026-11-12T09:00:00+00:00\"}")]
    public void Anything_else_is_ignored(string? state)
        => Assert.Null(PeopleReviewUndo.Read(state, "reviewer", Now));
}
