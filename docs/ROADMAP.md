# Beloved Platform & Assembly Engine Roadmap

This document outlines the strategic steps to transition the Beloved assembly model from a regex-based compiler into a context-aware, AST-analyzing **Deterministic AI Stitching Engine**.

## Phase 1: Hybrid Parser & Interface Contract (Complete)
- **Goal**: Introduce the capability for LLM providers to receive file source code and structured merge instructions.
- **Implementation**:
  - Extended the `ILlmProvider` contract with `StitchFileAsync`.
  - Implemented the contract across all provider backends (`Ollama`, `Gemini`, `Claude`, `OpenAI`).
  - Added an optional `ILlmProvider` pass to `AssemblyCompiler` that activates when regex placeholders `{/* MODULE_NAV_ITEMS_START */}` are missing or altered.
  - Verified 100% test coverage with mock integration tests.

## Phase 2: AST Merging Engine & Code Analysis Validation (Complete)
- **Goal**: Eliminate plaintext merges. Parse incoming files and module templates into Abstract Syntax Trees (AST).
- **Implementation**:
  - Integrated `Microsoft.CodeAnalysis.CSharp` (Roslyn) for .NET API dbset class additions via `RoslynDbContextMerger`.

## Phase 3: Build Verification & Self-Healing Loop (Complete)
- **Goal**: Auto-detect syntax/compile errors during local assembly and recursively feed build errors back to the LLM for self-correction.
- **Implementation**:
  - Run AST Diagnostics checks inside `AssemblyCompiler` utilizing Roslyn, with automated prompt feedback hooks to auto-heal errors.

## Phase 4: Dynamic Theme & Layout Adaptation (Complete)
- **Goal**: Enable modules to inherit target styles dynamically.
- **Implementation**:
  - Parsed `index.css` root color schemes and supplied them to LLM stitching hints.

## Phase 5: Dependency Graphs & API Gateway Stitching (Complete)
- **Goal**: Topologically resolve modular requirements and construct API routing proxies.
- **Implementation**:
  - Created `DependencyResolver` sorting DAG pipelines topologically.
  - Added `BuildSandbox` to generate gateway routing (YARP configuration) dynamically.
