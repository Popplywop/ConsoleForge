#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace ConsoleForge.SourceGen;

/// <summary>
/// An immutable array wrapper with structural value equality, safe for use as a field
/// in Roslyn incremental generator data models. Two <see cref="EquatableArray{T}"/>
/// values are equal iff they contain the same elements in the same order according to
/// <see cref="EqualityComparer{T}.Default"/>.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
{
    // null and empty are treated identically — both mean "no items".
    private readonly T[]? _items;

    /// <summary>An empty array.</summary>
    public static readonly EquatableArray<T> Empty = default;

    /// <param name="items">Source collection; may be null or empty.</param>
    public EquatableArray(IList<T>? items)
    {
        if (items is null || items.Count == 0)
            _items = null;
        else
        {
            var arr = new T[items.Count];
            items.CopyTo(arr, 0);
            _items = arr;
        }
    }

    /// <summary>Number of elements.</summary>
    public int Count => _items?.Length ?? 0;

    /// <summary>Returns the underlying array, never null.</summary>
    public T[] AsArray() => _items ?? [];

    // ── IEquatable ──────────────────────────────────────────────────────

    public bool Equals(EquatableArray<T> other)
    {
        var a = _items;
        var b = other._items;

        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length)   return false;

        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Length; i++)
            if (!cmp.Equals(a[i], b[i])) return false;

        return true;
    }

    public override bool Equals(object? obj)
        => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_items is null) return 0;

        var cmp = EqualityComparer<T>.Default;
        unchecked
        {
            int hash = 17;
            foreach (var item in _items)
                hash = hash * 31 + (item is null ? 0 : cmp.GetHashCode(item));
            return hash;
        }
    }

    /// <summary>Structural equality.</summary>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
        => left.Equals(right);

    /// <summary>Structural inequality.</summary>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
        => !left.Equals(right);

    // ── IEnumerable ─────────────────────────────────────────────────────

    public IEnumerator<T> GetEnumerator()
        => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}