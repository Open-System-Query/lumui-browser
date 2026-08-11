# LUMUI specification

Specification: 1.0  
Surface document: 1.0  
Action message: 1.0  
Component catalogue: 1.0  
Component specification: 1.0  
Web protocol: 1.0

## 1. Purpose

LUMUI is a semantic user-interface system. An application describes what a
person can see and do. A viewer validates that description and composes an
interface for the current device, input method, output and person.

The application owns information, state, validation and actions. The viewer
owns layout, typography, control presentation, focus, navigation and
device-specific composition.

A surface is useful without one prescribed visual design. The same surface can
be presented as a browser page, desktop workspace, phone flow, watch card,
kiosk task, appliance panel, printed document, voice conversation or
screen-reader-first experience.

The schemas and component catalogue are the exact machine-readable contracts.
The normative [LUMUI Components](lumui-components.md) companion defines every
standard component's fields, semantics, fallback and presentation behavior.
This document explains how those contracts work together.

The words MUST and MUST NOT define requirements. SHOULD and SHOULD NOT define
expected behaviour with documented exceptions. MAY defines an option.

## 2. Working model

LUMUI has four parts:

1. An **application** publishes its current interface state.
2. A **surface** describes pages, components, actions and relationships.
3. A **viewer** validates the surface and chooses a suitable composition.
4. An **action** sends a named intention back to the application.

```text
Application → Surface → Viewer → Person
     ↑                     ↓
     └──────── Action ─────┘
```

Applications do not send screen coordinates, CSS or executable interface code.
Viewers do not invent business rules or modify the meaning of actions.

## 3. Surface document

A surface is a JSON object using media type `application/lumui+json`.

It contains:

- `lumui_surface`: surface contract version;
- `app_id`: stable application identity;
- `surface_id`: stable identity for the represented state;
- `revision`: non-negative state revision;
- `title` and optional description;
- locale and text direction;
- application identity and optional brand tokens;
- the requested page and available pages;
- semantic navigation;
- declared actions;
- metadata and typed links.

The schema rejects undeclared fields. A viewer MUST validate a surface before
rendering or invoking actions.

Every page has a stable ID, title, role and ordered regions. Every component has
a stable ID and a registered `kind`. IDs address meaning and actions; they are
not CSS selectors or layout instructions.

Example:

```json
{
  "lumui_surface": "1.0",
  "app_id": "com.example.reception",
  "surface_id": "reception.welcome",
  "revision": 7,
  "title": "Welcome",
  "locale": "en",
  "direction": "ltr",
  "requested_page_id": "welcome",
  "pages": [
    {
      "id": "welcome",
      "title": "Welcome",
      "role": "application",
      "regions": [
        {
          "id": "welcome.intro",
          "kind": "section",
          "role": "introduction",
          "priority": "high",
          "items": [
            {
              "id": "welcome.heading",
              "kind": "text",
              "text_role": "heading",
              "text": "Welcome"
            }
          ]
        }
      ]
    }
  ],
  "actions": {},
  "links": [
    {
      "rel": "self",
      "href": "/",
      "type": "application/lumui+json"
    }
  ]
}
```

## 4. Pages, regions and navigation

A page is a logical view or workflow step. A region groups related meaning,
such as an introduction, form, summary, status area or call to action.

Regions use semantic roles and priority. These allow a viewer to decide:

- what must remain visible;
- what can move to another pane or step;
- what belongs in a glanceable view;
- what may be disclosed on demand;
- what should be spoken or printed first.

Navigation is a graph of stable routes and pages. A viewer may present the same
navigation as links, tabs, a rail, a menu, gestures, spoken choices or hardware
controls. The destination and meaning MUST remain unchanged.

A viewer MAY present related regions or pages together when their declared
relationships and current state make that safe. It MUST NOT duplicate an action
in a way that makes its state or result ambiguous.

## 5. Components

The component catalogue defines the supported semantic vocabulary and the
closed field set for every kind. The normative [LUMUI Components](lumui-components.md)
companion defines all 73 standard kinds, all component fields and structured
value types, and the required or recommended presentation of each kind.
Together with the surface schema it is part of this specification, not optional
design guidance.

Component families include:

- structure and navigation;
- text and values;
- links and commands;
- forms and choices;
- status, progress and feedback;
- images, audio, video and graphics;
- device-mediated capabilities;
- preview containers.

A component declares meaning, state and available actions. It does not prescribe
pixel coordinates or imitate a platform control with arbitrary graphics.

A renderer SHOULD use a native control when the target environment provides a
suitable one. Otherwise it uses an accessible equivalent that preserves label,
value, validation, state and action identity. It MUST implement every component
behavior marked `MUST` in the component specification and SHOULD follow each
presentation recommendation unless the target environment requires an
equivalent presentation.

The `preview` component contains one semantic component and gives the renderer
an explicit context for a preview-specific layout. The contained component is
processed normally, including its state, accessibility information and actions.
Preview does not identify another surface and does not change the device,
interaction mode or accessibility context. A renderer without a dedicated
preview layout MUST render the contained component without that layout.

Per-kind forbidden fields override the common optional field set. In particular,
`passwordField` and `otpField` forbid `value` and `default_value` in a published
surface. Secret entry is represented only by `value_present`; a password renderer
MUST keep entered characters as viewer-owned transient action input, MUST mask
them by default, conventionally with dots or asterisks, and MUST never place
the secret in surface state, logs, history or previews. A temporary reveal is
permitted only under the explicit conditions in the component specification.

Unsupported components MUST produce a meaningful fallback. Required content or
actions must never disappear silently.

Compact devices may need shorter visible wording. A component MAY provide a
registered compact label or summary when the catalogue permits it. The full
accessible name and original meaning remain available.

## 6. Device composition

A render profile describes a device family and viewing context. A viewer may
maintain more detailed internal profiles for screen geometry, safe areas,
input, distance, privacy and output.

A profile changes composition, not meaning.

Examples:

- a desktop may use a navigation rail and master-detail workspace;
- a tablet may use one or two touch-oriented panes;
- a phone may use progressive disclosure and reachable bottom actions;
- a watch may show one glanceable card or guided step at a time;
- a kiosk may use a large step-by-step public workflow;
- a round appliance may place one value and immediate actions in its safe area;
- a badge may select an exact-size non-interactive print composition;
- a voice viewer may turn the page into prompts and named choices.

A materially different profile MUST receive a suitable composition. Merely
scaling a desktop page is not sufficient.

Every detailed device profile should define:

- viewport and outer shell geometry;
- display mask and safe area;
- orientation;
- input capabilities;
- output capabilities;
- target-size range;
- navigation pattern;
- scrolling or paging policy;
- preferred presentation;
- attention distance and density;
- any protected system areas.

A viewer MUST detect horizontal overflow, unintended clipping, inaccessible
actions and unsafe placement. It then recomposes, paginates or offers a suitable
handoff. It MUST NOT reduce content below a person's requested text or control
size merely to force a fit.

No-scroll profiles use pages, gestures, rotary input, hardware controls or
another declared interaction. Printed outputs never gain interactive controls
or scrollbars.

## 7. Presentation and appearance

Presentation and appearance are independent.

Presentation controls how information is paced:

- `standard`: the complete task-oriented view;
- `guided`: one meaningful step at a time;
- `focus`: primary content without supporting distraction;
- `glance`: essential state and immediate actions.

A device has a sensible automatic presentation, but a viewer may let the person
choose another compatible mode.

Appearance controls visual character through renderer-owned tokens such as
colour, type, radius, elevation, spacing and motion. Appearance MUST NOT change
information, validation, permissions, enabled actions or reading order.

Publisher brand tokens are suggestions inside the rendered application.
Accessibility requirements and viewer policy take precedence.

## 8. Accessibility and personal preferences

Accessibility is part of the rendering contract, not a separate theme.

A viewer SHOULD support composable preferences for:

- text size, typeface and weight;
- line, word, letter and paragraph spacing;
- readable line length and alignment;
- contrast and colour scheme;
- colour intensity;
- reduced motion and transparency;
- control and target size;
- visible focus;
- reading aids and reduced distraction;
- screen-reader, voice and other output modes.

Preferences are applied before final composition. When larger text or controls
no longer fit, the viewer changes columns, disclosure, navigation or page count.
It does not overlap, crop or silently undo the preference.

Changing colour alone MUST NOT change geometry. Alternative palettes should be
derived from semantic colour roles rather than by applying a visual filter to
the complete interface.

Every interactive component needs an accessible name, visible focus, logical
order and an input-independent action. Colour is never the only expression of
state.

## 9. Actions and state

Actions are declared by stable ID. A component refers to an action rather than
embedding executable behaviour.

An action definition declares:

- callback identity;
- confirmation level;
- idempotency behaviour;
- a closed input schema.

An invocation contains:

- message and protocol version;
- unique message ID;
- surface ID and revision;
- component and action IDs;
- validated input;
- source context.

The application validates identity, authorization, revision, input and domain
rules before changing state. It returns a correlated action result containing
status, a message and an optional next surface.

Viewers ask for implicit confirmation before sending an action. Explicit and
dangerous actions use the confirmation challenge defined by the action-message
schema. Idempotent actions use a replay-safe key.

## 10. Web publication

An ordinary HTTP or HTTPS route can provide both HTML and LUMUI.

A viewer requests the entered URL with:

```http
Accept: application/lumui+json, text/html;q=0.8
```

The route SHOULD return its surface directly when it supports content
negotiation. An HTML response can advertise its surface and service descriptor:

```html
<link rel="alternate"
      type="application/lumui+json"
      href="/current-route/">
<link rel="service-desc"
      type="application/lumui+json"
      href="/lumui/descriptor.json">
```

The same relationships may be sent in the HTTP `Link` header.

An origin SHOULD expose discovery at `/.well-known/lumui`. Discovery identifies
the service descriptor; its URL is publisher-defined, with
`/lumui/descriptor.json` used by the reference implementation. Action,
surface and supporting-resource URLs are declared by discovery, descriptors or
links and MUST NOT be inferred from a private path convention. Normal HTML
fallbacks remain available at the same logical application routes.

Relative URLs resolve against the response that declares them. Viewers accept
only supported safe schemes and enforce normal browser origin rules.

The entered logical URL remains the history entry. Fetching a descriptor or
alternate representation does not silently replace it.

## 11. Validation, trust and privacy

The viewer is a trust boundary. It validates documents before rendering and
never executes publisher JavaScript, HTML or CSS as part of a LUMUI surface.

Viewer-owned areas include:

- address and navigation controls;
- identity and connection information;
- permissions;
- dangerous confirmation;
- downloads and external-resource policy;
- developer diagnostics.

Applications remain responsible for authentication, authorization, business
validation, data retention and accurate content.

Sensitive values SHOULD be marked with the applicable schema field. Viewers
avoid placing them in history, logs, previews or shared output.

Resource size, redirects, request duration and nesting depth MUST be bounded.
Errors use readable messages and may use `application/problem+json` for
machine-readable detail.

## 12. Framework-independent implementation

LUMUI is defined by documents, schemas, media types and behaviour. It does not
require a particular programming language, framework, server or UI toolkit.

Any implementation is compatible when it:

1. produces valid surfaces and action messages;
2. preserves stable identities and revisions;
3. validates closed schemas;
4. preserves meaning across supported compositions;
5. provides required accessibility behaviour;
6. follows the web and action contracts it claims.

A web application may include an HTML renderer so ordinary browsers receive a
complete page while compatible viewers receive the structured representation.
Native and specialised viewers can map the same components to their own
controls.

## 13. Compatibility and evolution

A compatibility claim names:

- specification and surface versions;
- publisher, renderer or viewer conformance class;
- supported profiles and outputs;
- component families.

Implementations MAY provide custom renderers for standard component kinds,
including registered sandboxed renderers selected by the `renderer` field of
`graphic`. A custom renderer MUST preserve the standard kind's declared
meaning, fields, fallback, validation and accessibility behavior. It MUST NOT
introduce ad hoc component kinds or bypass the closed component catalogue. New
component semantics require a versioned update to the catalogue, schema and
component specification.

Compatibility requires behaviour, not visual similarity. Two conforming
viewers may look different while preserving the same information, state,
validation, actions and accessible operation.

## 14. Reference resources

The reference website publishes:

- `/specification/protocol.json`
- `/specification/component-catalog.json`
- `/specification/lumui-components.md`
- `/specification/schemas/surface.schema.json`
- `/specification/schemas/service-descriptor.schema.json`
- `/specification/schemas/discovery.schema.json`
- `/specification/schemas/action-message.schema.json`
- `/specification/lumui-specification.md`

The component catalogue and schemas are authoritative for structural validity,
fields and values. `lumui-components.md` is authoritative for component semantics
and presentation behavior. Examples explain the contract but never override it.
The `/specification/lumui-components.md` resource is the raw, versioned
Markdown document for implementations and offline use; `/components/` is the
human-oriented web presentation of the same component vocabulary.
