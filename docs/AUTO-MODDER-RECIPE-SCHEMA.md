# Auto Mod Recipe v1

Status: draft contract implemented by the Auto Modder foundation.

## Stable identity

Recipe schema is mhw-auto-mod-recipe/v1. Recipe IDs use the mhw.auto-mod.<publisher>.<name> namespace and are independent of display names. Recipe versions are dotted numeric versions.

## Top-level fields

A v1 recipe contains schema, id, version, name, game, requires, inputs, steps, and outputs.

requires.adapters is an array of adapter ID/version-range pairs. inputs is an object keyed by lower_snake_case input IDs. steps is an ordered array. outputs is an ordered array of source alias plus manager-relative output path.

The runtime currently limits a recipe to 64 inputs, 256 steps, and 64 outputs.

## Input contract

Supported normalized input kinds are integer, decimal, string, boolean, enum, entity, file, image, color, list, and group.

Common metadata includes label, required, default, min, max, pattern, options, catalog, and advanced.

Entity values are structured objects and must contain a non-null id property. Entity recipes must declare the catalog that gives that ID meaning. Enum inputs must declare at least one option.

The foundation validates JSON type, numeric bounds, enum membership, bounded regular expressions, unknown inputs, and missing required inputs. Catalog membership resolution is a later engine slice.

## Expression grammar

v1 expressions deliberately start smaller than the long-term plan.

Accepted expression:

    ${identifier}
    ${identifier.property}
    ${identifier.property.property}

References may have at most four identifier segments. Identifiers are lower_snake_case style tokens. A reference must occupy the entire scalar string; mixed interpolation is not accepted.

A property walk may only traverse the already-resolved input JSON value. This supports entity/catalog property access such as ${armor.id} without reflection, filesystem access, network access, process launch, dynamic code, or arbitrary evaluation.

Arithmetic, conditionals, formatting functions, and general scripting are reserved for a later schema revision or an explicitly bounded grammar extension. Unknown expressions fail validation instead of falling through to an evaluator.

## Step contract

Each step has:

- id: stable lower_snake_case step identity.
- adapter: a declared adapter ID.
- op: one typed patch operation.
- source: declared source alias.
- target: optional scalar selector or bounded reference.
- field: optional field path or bounded reference.
- expect: optional old-value assertion.
- value: optional new value.

Typed operations in the v1 IR are select_record, assert_field, set_field, set_bit_field, replace_enum, replace_reference, insert_record, delete_record, copy_record, replace_asset, and write_output.

An adapter must explicitly advertise support for every operation planned against it.

## Output contract

Outputs use manager-relative paths accepted by the existing PathRules policy. Rooted paths, traversal, reserved-device paths, alternate data streams, and paths outside nativePC or root fail closed. Duplicate normalized output paths are rejected.

## Public machine-readable schema

See docs/schemas/mhw-auto-mod-recipe-v1.schema.json.

The JSON Schema is the public structural contract. Runtime validation remains authoritative for semantic checks such as adapter capabilities, version negotiation, path safety, and cross-field rules.
