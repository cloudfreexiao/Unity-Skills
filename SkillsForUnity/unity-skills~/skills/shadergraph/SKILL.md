---
name: unity-shadergraph
description: "Shader Graph creation, inspection, and constrained blackboard editing. Use when users want to create shadergraph/subgraph assets, inspect nodes and edges, or edit Shader Graph properties and keywords. Triggers: Shader Graph, shadergraph, sub graph, blackboard property, shader keyword, 着色图, 子图, ShaderGraph 属性, ShaderGraph 关键字."
---

# ShaderGraph Skills

Shader Graph asset workflows for Unity 2022.3+ with source-backed template and serialization handling.

## Guardrails

**Mode**: Full-Auto required

**Routing**:
- For HLSL text shaders: use `shader_*`
- For Shader Graph assets and Sub Graphs: use this module
- First version only supports graph creation, reading, and blackboard `Property/Keyword` editing
- Node-level editing is intentionally constrained and deferred

**Runtime-first rules**:
- Always call `shadergraph_list_templates` before assuming a named graph template exists
- If `shadergraph_create_graph` returns a warning about missing `GraphTemplates`, treat it as expected package-version behavior, not as a failure
- Use `shadergraph_get_info`, `shadergraph_get_structure`, `shadergraph_list_properties`, and `shadergraph_list_keywords` to inspect the real graph state before editing
- For keyword updates/removals, use `displayName` or `referenceName` from the live graph data; do not invent identifiers

**Validated behavior**:
- Unity 2022.3 + `com.unity.shadergraph@14.0.12` does not ship `GraphTemplates/`; `shadergraph_create_graph` falls back to blank graph creation
- Unity 6 ShaderGraph packages may provide actual template directories; in that case template listing and template copy creation remain available

## Skills

### `shadergraph_list_templates`
List Shader Graph templates shipped by the installed package.

### `shadergraph_create_graph`
Create a Shader Graph asset from a package template.

### `shadergraph_create_subgraph`
Create a blank Shader Sub Graph asset with a configured output slot.

### `shadergraph_list_assets`
List Shader Graph and Sub Graph assets in the project.

### `shadergraph_get_info`
Get a high-level summary of a Shader Graph or Sub Graph asset.

### `shadergraph_get_structure`
Inspect nodes, edges, properties, and keywords inside a Shader Graph asset.

### `shadergraph_list_properties`
List graph blackboard properties.

### `shadergraph_add_property`
Add a constrained blackboard property.

### `shadergraph_update_property`
Update a constrained blackboard property.

### `shadergraph_remove_property`
Remove a graph property.

### `shadergraph_list_keywords`
List graph blackboard keywords.

### `shadergraph_add_keyword`
Add a graph keyword.

### `shadergraph_update_keyword`
Update a graph keyword.

### `shadergraph_remove_keyword`
Remove a graph keyword.

### `shadergraph_reimport`
Force reimport of a Shader Graph asset after external edits.

---
## Exact Signatures

Exact names, parameters, defaults, and returns are defined by `GET /skills/schema` or `unity_skills.get_skill_schema()`, not by this file.
