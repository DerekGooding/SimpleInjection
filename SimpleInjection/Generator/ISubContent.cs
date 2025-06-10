﻿namespace SimpleInjection.Generator;

public interface ISubContent<Tkey, Tvalue> where Tkey : INamed
{
    Dictionary<Tkey, Tvalue> ByKey { get; }

    Tvalue this[Tkey key] { get; }
}
