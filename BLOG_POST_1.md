# Blog Post Outline: Introducing SimpleInjection

**Title:** Introducing SimpleInjection: The .NET DI Container with Built-in Content Management

**Target Audience:** .NET developers, C# developers, game developers.

**Platforms:** dev.to, Medium, personal blog.

---

### Introduction

- **Hook:** Tired of manually keeping enums and data collections in sync? What if your DI container could do it for you?
- **Problem:** Introduce the common problem of managing static data (e.g., item catalogs, configurations) in .NET applications. This often involves brittle code, "magic strings," and manual synchronization between data arrays/lists and enums.
- **Solution:** Introduce `SimpleInjection` as a lightweight DI library with a unique, built-in solution for this problem: source generation for content management.

### What is SimpleInjection?

- Briefly explain the two core features:
    1.  **Simple Dependency Injection:** Attribute-based (`[Singleton]`, `[Scoped]`, `[Transient]`), zero-config DI.
    2.  **Content Source Generation:** Automatically generates enums and type-safe accessors from your data collections.
- Highlight the seamless integration between the two.

### Quick Start: A 5-Minute Example

- Walk through a very simple "Hello World" example.
- Show a simple service (`[Singleton]`) being injected.
- Show a simple content class (`IContent<T>`) and the automatically generated enum.
- Provide clear, copy-pasteable code snippets.

### Why Not Just Use the Default DI Container?

- Acknowledge that `Microsoft.Extensions.DependencyInjection` is the standard.
- Use the comparison table from the `README.md` to highlight the key differences.
- **Positioning:** `SimpleInjection` isn't a replacement for the default container in all cases. It's a specialized tool for projects where its content generation feature provides a significant advantage.

### A More Realistic Scenario: Managing Game Items

- Use the game inventory example from `EXAMPLES.md`.
- Show how it solves a real-world problem, making the code cleaner and safer.
- Emphasize the benefits: no magic strings, compile-time safety, and improved developer productivity.

### Under the Hood: How Does it Work?

- Briefly explain the role of C# Source Generators.
- Mention the Roslyn analyzers that help enforce performance best practices (`NamedComparer<T>`). This adds a "pro" touch.

### Conclusion

- **Summary:** Recap the main benefits of `SimpleInjection`: simplicity, reduced boilerplate, and type-safety for content management.
- **Call to Action:**
    - "Give it a try! `dotnet add package SimpleInjection`"
    - "Check out the project on GitHub [link]."
    - "We welcome feedback and contributions!"
