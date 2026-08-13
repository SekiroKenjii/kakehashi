# 0012. Chips wrap with a custom WrapPanel, not UniformGridLayout

Date: 2026-08-12
Status: accepted

## Context

WinUI ships no wrapping panel. The nearest built-in, `UniformGridLayout`, is uniform by
definition: it measures the first item and gives every cell that width. For role/tag chips that
failed in both configurations. Sized to content, a short first chip set the cell width and any
longer chip had its remove button clipped off the end; forced to a fixed column count, a
two-letter role stretched across half the panel with its × pushed to the far right. A chip must
be exactly as wide as the word inside it, which requires a panel that measures each child
individually rather than deciding one size for all of them.

## Decision

`WrapPanel` (client/src/Shared/Kakehashi.UI.Common/Controls/WrapPanel.cs) is a custom `Panel`
that lays children left to right at their own desired width and wraps to the next line when the
row runs out of room. `HorizontalSpacing` and `VerticalSpacing` dependency properties control the
gaps. It deliberately does not virtualize: the lists it serves hold a handful of roles or tags,
so a virtualizing layout would cost a recycling context and a realization window to save nothing.

## Consequences

Every chip gets its intrinsic width and no remove button is ever clipped or marooned. Layout
invariants a future change must respect: children are measured with width constrained and height
unbounded, because width alone decides where to wrap and the panel's height is the output, not an
input; and the first child on a line always stays on that line however wide it is — moving it
down would leave an empty line above and still not make it fit. The cost is owning measure and
arrange logic ourselves, and the non-virtualizing design means the panel is wrong for large
collections; anything item-heavy should use `ItemsRepeater` with a virtualizing layout instead.
