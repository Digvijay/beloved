# Beloved Platform & Assembly Engine Roadmap

This document outlines the strategic steps to transition the Beloved assembly model from a regex-based compiler into a context-aware, AST-analyzing **Deterministic AI Stitching Engine**.

## Phase 1: Hybrid Parser & Interface Contract (Complete)
- **Goal**: Introduce the capability for LLM providers to receive file source code and structured merge instructions.
- **Implementation**:
  - Extended the `ILlmProvider` contract with `StitchFileAsync`.
  - Implemented the contract across all provider backends (`Ollama`, `Gemini`, `Claude`, `OpenAI`).
  - Added an optional `ILlmProvider` pass to `AssemblyCompiler` that activates when regex placeholders `{/* MODULE_NAV_ITEMS_START */}` are missing or altered.
  - Verified 100% test coverage with mock integration tests.

## Phase 2: AST Merging Engine & Code Analysis Validation
- **Goal**: Eliminate plaintext merges. Parse incoming files and module templates into Abstract Syntax Trees (AST) before sending them to the LLM.
- **Tasks**:
  - Integrate `Microsoft.CodeAnalysis.CSharp` (Roslyn) for .NET API stitching.
  - Integrate a lightweight TypeScript/TSX AST parser/visitor pattern on the frontend or backend helper.
  - Formulate structured JSON instruction sets for the LLM that specify code modifications in AST node operations (e.g., `AddImportStatement`, `AddRouteElement`, `AddDbSet`).

## Phase 3: Zero-Dependency Fallback & Self-Healing Builds
- **Goal**: Auto-detect syntax/compile errors during local assembly and recursively feed build errors back to the LLM for self-correction.
- **Tasks**:
  - Run quick check compiles (`dotnet build` / `vite build`) in-memory or on sandbox paths.
  - If errors occur, send the compiler log and offending code segment to the LLM to trigger a refinement cycle.

## Phase 4: Dynamic Theme & Layout Adaptation
- **Goal**: Enable modules to read and inherit the global styling system of the host application dynamically (e.g., adapt layout views to CSS, Tailwind, or CSS Modules of the target container).
