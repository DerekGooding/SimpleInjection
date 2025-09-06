# Blog Post Outline: Deep Dive into Source Generation

**Title:** Stop Writing Boilerplate: Auto-Generate Enums from Your Data in C# with SimpleInjection

**Target Audience:** Intermediate to advanced C# developers, developers interested in .NET internals, Roslyn, and source generators.

**Platforms:** dev.to, Medium, personal blog, C#/.NET specific communities.

---

### Introduction

- **Hook:** Show a "before" and "after" code snippet.
    - **Before:** A manually maintained enum and a separate `switch` statement or dictionary to map it to data. Highlight the fragility.
    - **After:** An `IContent<T>` implementation and the clean, type-safe usage with a generated enum.
- **Thesis:** C# Source Generators are a powerful tool for metaprogramming, and `SimpleInjection` provides a practical, real-world example of how they can be used to improve code quality and developer experience.

### The Problem with Manual Content Management

- Go into more detail about the pain points:
    - **Synchronization Bugs:** Adding a new item to the data collection but forgetting the enum, or vice-versa.
    - **Refactoring Nightmares:** Renaming an item requires changes in multiple places.
    - **Runtime Errors:** Using `Enum.Parse` with a string that doesn't match, or casting an integer that is out of bounds.

### How SimpleInjection Solves This with a Source Generator

- Explain the core mechanism:
    1. The developer defines a class that implements `IContent<T>` where `T` implements `INamed`.
    2. The source generator scans the project for these classes during compilation.
    3. It reads the `All` property to get the list of items.
    4. It generates a new `partial` class containing:
        - An `enum` with a member for each item's `Name`.
        - Helper methods and properties for easy access (`Get(MyEnum type)`, `this[MyEnum type]`, `MyItemName`).
- Show the actual generated code (or a snippet of it) to demystify the process.

### A Deeper Dive: Performance Matters

- This is where you can show off the more advanced features.
- Explain the `INamed` interface and why it's important.
- Introduce the `NamedComparer<T>` and explain how it can be faster than the default `EqualityComparer` for dictionary lookups on custom objects (avoids potential boxing or reflection-based comparisons depending on implementation).
- Mention the Roslyn analyzers (`NC001`, `TND001`) that guide the user to write performant code. This demonstrates a commitment to quality and performance.

### Can I Use This Without the DI Container?

- **Answer:** Yes! Although they are designed to work together, the source generator part can be used independently.
- Explain that as long as the attributes and interfaces from the `SimpleInjection.Generator` assembly are referenced, the generator will work, even if you don't use the `Host.Initialize()` DI features. This broadens the library's appeal. (I should verify this is true by looking at the project dependencies).

### Conclusion

- **Summary:** `SimpleInjection`'s source generator is a powerful tool for anyone working with data catalogs in .NET. It's a prime example of how modern C# features can solve long-standing problems.
- **Call to Action:**
    - "Explore the source code on GitHub to see how the generator is built."
    - "Think about other repetitive coding tasks in your projects that could be automated with source generators."
    - "Try out `SimpleInjection` in your next project!"
