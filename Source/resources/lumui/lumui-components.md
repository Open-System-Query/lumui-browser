# LUMUI Components

Component specification: 1.0  
Compatible surface: 1.0  
Component catalog: 1.0

## 1. Normative status

This document defines the semantics and presentation behavior of every standard LUMUI component. It is a normative companion to the [LUMUI specification](specification.md).

The [`component-catalog.json`](component-catalog.json) file defines the registered kinds, allowed fields, field requirements and fallbacks. The [`surface.schema.json`](schemas/surface.schema.json) file defines closed value shapes. This document defines what those values mean and the behavior a conforming renderer preserves.

`MUST` and `MUST NOT` are requirements. `SHOULD` and `SHOULD NOT` define the expected presentation unless the target environment requires an equivalent alternative. `MAY` identifies an option. A publisher MUST describe meaning and state rather than a platform-specific visual control. A renderer owns geometry and styling, but MUST preserve all declared information, state, validation, relationships and actions.

## 2. Component model

Every component requires `id` and `kind`. Fields listed as common optional fields are permitted on every kind unless promoted to required or explicitly forbidden by that kind. A forbidden field takes precedence over the common field set. Kind-specific fields are closed: a component containing an undeclared or forbidden field is invalid.

A requirement written as `a|b` in the machine-readable catalog means that at least one of those fields is required. A component MAY contain both when their meanings are compatible. Required fields cannot be made semantically absent by using empty strings, empty collections or null-like values that violate the schema.

Interactive components MUST have an accessible name, deterministic focus behavior and an input-independent activation path. Disabled, hidden, required, read-only, invalid, live and sensitive states MUST be honored by every output mode. Color, shape, position, motion or sound MUST NOT be the only carrier of meaning.

### Common required fields

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | `id` | Stable component identifier within the surface. |
| `kind` | `componentKind` | Registered component kind that selects its semantic contract. |

### Common optional fields

| Field | Type | Meaning |
| --- | --- | --- |
| `label` | `localizedString` | Primary human-readable name of the component. |
| `description` | `localizedString` | Supporting explanation of the component or its current value. |
| `help` | `localizedString` | Additional guidance available on request. |
| `enabled` | `boolean` | Whether the person may currently operate the component. |
| `visible` | `boolean` | Whether the component participates in the current presentation. |
| `priority` | `priority` | Semantic importance used during composition and disclosure. |
| `sensitive` | `sensitivity` | Sensitivity classification used by privacy and output policy. |
| `live` | `live` | Requested assistive announcement behavior for updates. |
| `required` | `boolean` | Whether a value or selection is required before completion. |
| `readonly` | `boolean` | Whether the value may be read but not changed. |
| `value` | `jsonValue` | Current scalar, structured or application-defined JSON value. |
| `default_value` | `jsonValue` | Value used when the person has not supplied one. |
| `action` | `id` | ID of the declared action invoked by the component. |
| `actions` | `array<id>` | Ordered IDs of declared actions offered by the component. |
| `error` | `errorState` | Current user-visible error state associated with the component. |
| `validation` | `validationState` | Current validity and validation message. |
| `fallback` | `component` | Standard semantic component used when specialized rendering is unavailable. |
| `metadata` | `object<jsonValue>` | Application metadata that does not alter standard component meaning. |

## 3. Complete component field reference

This table defines every top-level field accepted by a component. Structured values are expanded in the next section.

| Field | Type | Meaning |
| --- | --- | --- |
| `accept` | `string \| array<string>` | Accepted media types or file patterns. |
| `action` | `id` | ID of the declared action invoked by the component. |
| `actions` | `array<id>` | Ordered IDs of declared actions offered by the component. |
| `album` | `localizedString` | Album or collection associated with audio. |
| `allow_custom` | `boolean` | Whether a person may choose a value outside the supplied palette. |
| `allow_empty` | `boolean` | Whether no selection is a valid state. |
| `allow_half` | `boolean` | Whether half-step rating values are permitted. |
| `allow_negative` | `boolean` | Whether negative numeric values are permitted. |
| `allow_open_end` | `boolean` | Whether a date range may omit its end. |
| `allow_reveal` | `boolean` | Whether a viewer may offer temporary password revelation. |
| `alt` | `localizedString` | Text alternative that conveys the purpose of non-decorative media. |
| `artist` | `localizedString` | Artist or creator associated with audio. |
| `artwork` | `string (uri-reference) \| mediaResource` | Artwork URI or media resource associated with playback. |
| `attribution` | `localizedString` | Person or source to whom a quotation is attributed. |
| `audio_description` | `string (uri-reference)` | URI reference of an audio-description track or resource. |
| `auto_submit` | `boolean` | Whether complete input invokes submission without a separate command. |
| `autocapitalize` | `enum(none, sentences, words, characters)` | Requested automatic capitalization behavior. |
| `autocomplete` | `string` | Semantic autocomplete purpose understood by the host. |
| `axes` | `array<chartAxis>` | Definitions of chart axes. |
| `back_behavior` | `enum(auto, previous, home, close, blocked)` | Requested logical behavior when navigating back from a page. |
| `body` | `localizedString` | Main notification or message body. |
| `calendar` | `string` | Calendar system used to interpret a date. |
| `call_state` | `string` | Current state of a telephone call. |
| `capabilities` | `array<string>` | Capabilities required by a specialized renderer. |
| `caption` | `localizedString` | Text associated with and explaining content or media. |
| `captions` | `array<mediaTrack>` | Timed text tracks associated with media. |
| `category` | `string` | Application-defined notification category. |
| `center` | `geoPoint` | Geographic point initially centered by a map. |
| `chart_type` | `string` | Semantic visualization form requested for chart data. |
| `children` | `array<component>` | Ordered child components owned by the component. |
| `cite` | `string (uri-reference)` | URI reference identifying the source of a quotation. |
| `clear_action` | `id` | Declared action that clears the current value. |
| `collapsible` | `boolean` | Whether a structural group may be collapsed. |
| `columns` | `array<tableColumn>` | Definitions of table columns and their semantics. |
| `confirmation` | `enum(none, implicit, explicit, dangerous)` | Confirmation level associated with an offered command. |
| `contact` | `contact` | Structured contact associated with a dialer or communication control. |
| `content` | `string \| component` | Textual or nested semantic content owned by the component. |
| `content_extent` | `enum(tiny, small, medium, large)` | Expected content volume used for initial composition. |
| `content_type` | `string` | Semantic purpose or domain type of entered content. |
| `copy_action` | `id` | Declared action that copies represented content. |
| `copy_policy` | `string` | Policy describing how a selected file may be copied or retained. |
| `correlation_id` | `string` | Identifier used to correlate an error with diagnostics or support. |
| `credit` | `localizedString` | Credit associated with media or figure content. |
| `current_index` | `integer` | Zero-based index of the currently presented collection item. |
| `current_location` | `geoPoint` | Current geographic point when it is available and permitted. |
| `current_step` | `integer \| navigationStep` | Current route step index or structured maneuver. |
| `data` | `object<jsonValue>` | Closed or application-defined structured data for the component. |
| `debounce_ms` | `integer` | Delay before input changes may trigger an action. |
| `decorative` | `boolean` | Whether media carries no meaning and is omitted from accessibility output. |
| `default_value` | `jsonValue` | Value used when the person has not supplied one. |
| `description` | `localizedString` | Supporting explanation of the component or its current value. |
| `destination` | `geoPoint` | Geographic destination used for routing or selection. |
| `dirty` | `boolean` | Whether form values differ from their committed state. |
| `display_value` | `localizedString` | Human-readable representation of a numeric value. |
| `distance_remaining` | `number` | Remaining route distance in the declared unit or convention. |
| `download` | `boolean` | Whether a downloadable representation should be offered. |
| `duration_ms` | `integer` | Total media, route or message duration in milliseconds. |
| `editable` | `boolean` | Whether a combobox permits text entry in addition to selection. |
| `empty` | `component` | Semantic component shown when a collection has no items. |
| `enabled` | `boolean` | Whether the person may currently operate the component. |
| `end` | `string` | End value of a range. |
| `error` | `errorState` | Current user-visible error state associated with the component. |
| `estimated_count` | `integer` | Estimated total item count when the exact count is unavailable. |
| `eta` | `string (date-time)` | Estimated arrival date and time. |
| `expanded_node_ids` | `array<id>` | IDs of tree nodes currently expanded. |
| `expires_at` | `string (date-time)` | Date and time after which content is no longer current. |
| `external` | `boolean` | Whether navigation intentionally leaves the current application context. |
| `fallback` | `component` | Standard semantic component used when specialized rendering is unavailable. |
| `filter_mode` | `string` | Declared option-filtering behavior. |
| `filterable` | `boolean` | Whether table data supports filtering. |
| `format` | `enum(plain, markdown)` | Declared safe text format. |
| `help` | `localizedString` | Additional guidance available on request. |
| `high` | `number` | Upper threshold of a meter's noteworthy range. |
| `href` | `string (uri-reference)` | Navigation target URI reference. |
| `icon` | `string \| mediaResource` | Registered symbol name or media resource associated with a command. |
| `id` | `id` | Stable component identifier within the surface. |
| `illustration` | `string (uri-reference) \| mediaResource \| component` | Decorative or semantic illustration for an empty state. |
| `images` | `array<mediaResource>` | Ordered media resources in an image collection. |
| `indeterminate` | `boolean` | Whether progress has no measurable current value. |
| `integrity` | `string` | Integrity metadata used to verify specialized content. |
| `intent` | `string` | Purpose for which a file is selected. |
| `intrinsic_aspect_ratio` | `string` | Natural width-to-height proportion of media. |
| `items` | `array<component>` | Ordered nested components contained by the component. |
| `keyboard` | `string` | Preferred virtual keyboard or input layout hint. |
| `kind` | `componentKind` | Registered component kind that selects its semantic contract. |
| `label` | `localizedString` | Primary human-readable name of the component. |
| `language` | `string` | Language identifier of text, code or media. |
| `length` | `integer` | Expected number of entered characters or code positions. |
| `live` | `live` | Requested assistive announcement behavior for updates. |
| `low` | `number` | Lower threshold of a meter's noteworthy range. |
| `maneuvers` | `array<navigationStep>` | Ordered route guidance steps. |
| `markers` | `array<mapMarker>` | Geographic markers displayed or listed by a map. |
| `marks` | `array<valueMark>` | Named values shown along a range control. |
| `max` | `number \| string` | Maximum accepted or represented value. |
| `max_length` | `integer` | Maximum permitted character count. |
| `max_selected` | `integer` | Maximum permitted number of selected options. |
| `meaning` | `localizedString` | Accessible meaning of a symbol or icon. |
| `media_types` | `array<string>` | Accepted media categories for a system picker. |
| `message` | `localizedString` | Human-readable status, feedback or explanatory message. |
| `metadata` | `object<jsonValue>` | Application metadata that does not alter standard component meaning. |
| `min` | `number \| string` | Minimum accepted or represented value. |
| `min_length` | `integer` | Minimum permitted character count. |
| `min_selected` | `integer` | Minimum required number of selected options. |
| `mode` | `string` | Component-specific operating or selection mode. |
| `nodes` | `array<component>` | Root nodes of a hierarchical component collection. |
| `number` | `string` | Telephone number represented by a dialer. |
| `optimum` | `number` | Preferred value or range target of a meter. |
| `options` | `array<option>` | Available choices or commands represented as option objects. |
| `pagination` | `pagination` | Current page, page size and navigation references for a collection. |
| `palette` | `array<string>` | Permitted or suggested color values. |
| `password_manager` | `boolean` | Whether password-manager integration is permitted. |
| `pattern` | `string` | Validation pattern applied to entered text. |
| `placeholder` | `localizedString` | Short input hint shown only while no value is present. |
| `position_ms` | `integer` | Current media position in milliseconds. |
| `poster` | `string (uri-reference) \| mediaResource` | Poster URI or media resource shown before video playback. |
| `precision` | `integer` | Maximum or intended number of fractional digits. |
| `preload` | `enum(none, metadata, auto)` | Requested media preloading policy. |
| `preview` | `boolean` | Whether tentative combobox selection may be previewed. |
| `primary_action` | `id` | Declared primary action associated with a page. |
| `priority` | `priority` | Semantic importance used during composition and disclosure. |
| `purpose` | `localizedString` | Human-readable purpose of specialized content. |
| `readonly` | `boolean` | Whether the value may be read but not changed. |
| `regions` | `array<component>` | Ordered semantic regions owned by a page. |
| `rel` | `string` | Relationship of a link to its current resource. |
| `renderer` | `id` | Registered specialized renderer identifier. |
| `reorderable` | `boolean` | Whether collection items may be reordered. |
| `required` | `boolean` | Whether a value or selection is required before completion. |
| `requires_capabilities` | `array<string>` | Host capabilities required before offering a picker or operation. |
| `reset_action` | `id` | Declared action that restores form defaults. |
| `result_count` | `integer` | Current number of search results. |
| `role` | `string` | Semantic role used for structure or assistive presentation. |
| `route` | `route` | Structured route displayed by a map. |
| `route_summary` | `localizedString` | Concise textual description of a route. |
| `rows` | `array<array<jsonValue> \| object<jsonValue>>` | Table row values keyed or ordered according to columns. |
| `rules` | `array<localizedString>` | Human-readable password or input rules. |
| `secondary_actions` | `array<id>` | Ordered declared secondary actions associated with a page. |
| `selectable` | `boolean` | Whether read-only content may be selected or copied. |
| `selected` | `boolean \| id` | Selected state or selected component identifier. |
| `selection_mode` | `string` | Whether and how zero, one or many items may be selected. |
| `sensitive` | `sensitivity` | Sensitivity classification used by privacy and output policy. |
| `series` | `array<chartSeries>` | Named data series represented by a chart. |
| `session` | `mediaSession` | Structured media playback session state. |
| `session_id` | `id` | Stable identifier of a media playback session. |
| `severity` | `enum(info, success, warning, error, critical)` | Semantic urgency or impact of feedback. |
| `sortable` | `boolean` | Whether table columns may invoke sorting. |
| `source` | `string (uri-reference) \| mediaResource` | URI or typed media resource supplying component content. |
| `source_link` | `string (uri-reference) \| linkObject` | URI or link object identifying the source of a figure. |
| `spellcheck` | `boolean` | Whether host spell checking is requested. |
| `start` | `string` | Start value of a range. |
| `state` | `string` | Current semantic or playback state. |
| `state_description` | `localizedString` | Human-readable explanation of the current state. |
| `state_schema` | `object<jsonValue>` | Closed schema describing specialized graphic state. |
| `step` | `number` | Permitted numeric increment. |
| `step_minutes` | `integer` | Permitted time-entry increment in minutes. |
| `submit_action` | `id` | Declared action that submits a form or query. |
| `suggestions` | `array<option>` | Suggested option values associated with current input. |
| `summary` | `localizedString` | Accessible textual summary of complex content. |
| `symbol` | `string` | Registered symbolic icon identifier. |
| `table_fallback` | `component` | Tabular semantic fallback for a data visualization. |
| `tabs` | `array<component>` | Peer components exposed as selectable tabs. |
| `target` | `string (uri-reference)` | Destination URI or storage target of a file operation. |
| `text` | `localizedString` | Plain or localized textual content. |
| `text_role` | `enum(body, heading, lead, label, caption, code)` | Semantic role of text independent of visual styling. |
| `timezone` | `string` | Timezone used to interpret a date and time. |
| `title` | `localizedString` | Primary heading or title for the component. |
| `tone` | `enum(neutral, info, success, warning, danger)` | Non-exclusive semantic visual tone. |
| `transcript` | `string (uri-reference)` | URI reference of a textual media transcript. |
| `trigger` | `id` | Component ID that opens or owns a menu. |
| `type` | `string` | Media, column, link or application-defined type identifier. |
| `unit` | `string` | Unit associated with a numeric value or measurement. |
| `validation` | `validationState` | Current validity and validation message. |
| `validation_mode` | `enum(submit, change, blur)` | Event at which form validation is requested. |
| `value` | `jsonValue` | Current scalar, structured or application-defined JSON value. |
| `value_present` | `boolean` | Whether a secret or one-time value currently exists without exposing it. |
| `values` | `array<jsonValue>` | Current ordered set of selected or series values. |
| `variants` | `array<mediaResource>` | Alternative media resources for capability or density selection. |
| `visible` | `boolean` | Whether the component participates in the current presentation. |

## 4. Structured component values

These closed objects are used by component fields. Fields not listed for the applicable object are invalid.

### `tableColumn`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `id` | Required | `id` | Stable column identifier. |
| `label` | Required | `localizedString` | Column header. |
| `description` | Optional | `localizedString` | Additional explanation of the column. |
| `type` | Optional | `string` | Column data-type hint. |
| `unit` | Optional | `string` | Unit shared by values in the column. |
| `sortable` | Optional | `boolean` | Whether this column can request sorting. |

### `option`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `id` | Optional | `id` | Optional stable option identifier. |
| `label` | Required | `localizedString` | Human-readable option name. |
| `description` | Optional | `localizedString` | Supporting option explanation. |
| `value` | Required | `jsonValue` | Value submitted when selected. |
| `enabled` | Optional | `boolean` | Whether the option may be selected. |
| `selected` | Optional | `boolean` | Whether the option is currently selected. |
| `source` | Optional | `string (uri-reference) \| mediaResource` | Optional image or media associated with the option. |
| `alt` | Optional | `localizedString` | Text alternative for meaningful option media. |

### `mediaResource`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `src` | Required | `string (uri-reference)` | URI reference of the media resource. |
| `type` | Optional | `string` | Media type of the resource. |
| `alt` | Optional | `localizedString` | Text alternative for meaningful media. |
| `intrinsic_aspect_ratio` | Optional | `string` | Natural width-to-height proportion. |
| `duration_ms` | Optional | `integer` | Media duration in milliseconds. |
| `poster` | Optional | `string (uri-reference)` | Poster image URI for video. |
| `captions` | Optional | `array<object>` | Timed-text track descriptors. |
| `transcript` | Optional | `string (uri-reference)` | URI reference of a transcript. |
| `integrity` | Optional | `string` | Integrity value used to verify the resource. |
| `bytes` | Optional | `integer` | Expected resource size in bytes. |
| `variants` | Optional | `array<mediaResource>` | Alternative resources for capability or density selection. |

### `chartAxis`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `id` | Required | `id` | Stable axis identifier. |
| `label` | Required | `localizedString` | Human-readable axis name. |
| `unit` | Optional | `string` | Unit represented on the axis. |
| `min` | Optional | `number` | Minimum represented axis value. |
| `max` | Optional | `number` | Maximum represented axis value. |

### `chartSeries`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `id` | Required | `id` | Stable series identifier. |
| `label` | Required | `localizedString` | Human-readable series name. |
| `values` | Required | `array<number>` | Ordered numeric values in the series. |
| `unit` | Optional | `string` | Unit represented by the series. |

### `geoPoint`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `latitude` | Required | `number` | Latitude in decimal degrees. |
| `longitude` | Required | `number` | Longitude in decimal degrees. |
| `label` | Optional | `localizedString` | Human-readable place name. |

### `mapMarker`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `id` | Required | `id` | Stable marker identifier. |
| `position` | Required | `geoPoint` | Geographic marker position. |
| `label` | Optional | `localizedString` | Human-readable marker name. |
| `description` | Optional | `localizedString` | Supporting marker details. |
| `action` | Optional | `id` | Declared action associated with the marker. |

### `navigationStep`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `instruction` | Required | `localizedString` | Human-readable maneuver instruction. |
| `distance` | Optional | `number` | Distance associated with the step. |
| `duration_ms` | Optional | `integer` | Expected step duration in milliseconds. |
| `position` | Optional | `geoPoint` | Geographic point associated with the step. |

### `contact`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `name` | Required | `localizedString` | Human-readable contact name. |
| `phone` | Optional | `string` | Telephone number. |
| `email` | Optional | `string (email)` | Email address. |

### `pagination`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `page` | Required | `integer` | Current one-based page number. |
| `page_size` | Required | `integer` | Maximum items requested per page. |
| `total` | Optional | `integer` | Known total number of items. |
| `next` | Optional | `string (uri-reference)` | URI reference of the next page. |
| `previous` | Optional | `string (uri-reference)` | URI reference of the previous page. |

### `route`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `destination` | Required | `geoPoint` | Route destination. |
| `summary` | Optional | `localizedString` | Concise textual route description. |
| `distance` | Optional | `number` | Total route distance. |
| `duration_ms` | Optional | `integer` | Expected route duration in milliseconds. |
| `steps` | Optional | `array<navigationStep>` | Ordered route guidance steps. |

### `mediaTrack`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `src` | Required | `string (uri-reference)` | URI reference of the timed-text track. |
| `language` | Required | `string` | Language tag of the track. |
| `label` | Optional | `localizedString` | Human-readable track name. |
| `kind` | Optional | `enum(captions, subtitles, descriptions, chapters)` | Track purpose: captions, subtitles, descriptions or chapters. |

### `mediaSession`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `id` | Required | `id` | Stable playback-session identifier. |
| `state` | Required | `string` | Current playback state. |
| `source` | Optional | `string (uri-reference) \| mediaResource` | URI or typed media resource being played. |
| `position_ms` | Optional | `integer` | Current playback position in milliseconds. |
| `duration_ms` | Optional | `integer` | Total playback duration in milliseconds. |

### `valueMark`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `value` | Required | `number` | Numeric position of the mark. |
| `label` | Required | `localizedString` | Human-readable mark label. |

### `errorState`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `message` | Required | `localizedString` | Human-readable error message. |
| `code` | Optional | `string` | Application-defined error code. |

### `validationState`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `valid` | Required | `boolean` | Whether the current component value is valid. |
| `message` | Optional | `localizedString` | Human-readable validation result or correction. |

### `linkObject`

| Field | Requirement | Type | Meaning |
| --- | --- | --- | --- |
| `rel` | Required | `string` | Relationship to the current resource. |
| `href` | Required | `string (uri-reference)` | Target URI reference. |
| `type` | Optional | `string` | Media type of the target. |
| `title` | Optional | `string` | Human-readable target title. |
| `hreflang` | Optional | `string` | Language tag of the target. |

## 5. Components


## 5.1 Structure

### `page`

Logical view or workflow step.

Required fields: `id`, `kind` = `page`, `title`, `regions`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `role`, `primary_action`, `secondary_actions`, `back_behavior`

Presentation: Present as one logical view or workflow step with a clear title, deterministic reading order and navigation to peer pages. Only the requested page SHOULD be primary at one time.

### `section`

Group of related content.

Required fields: `id`, `kind` = `section`, `items`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `role`, `collapsible`

Presentation: Group the items under a semantic heading when a label is present. Visual containment MAY vary, but role, order and collapsible state MUST remain available.

### `form`

Inputs submitted as one unit.

Required fields: `id`, `kind` = `form`, `items`, `actions`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `error`, `validation`, `fallback`, `metadata`, `submit_action`, `reset_action`, `validation_mode`, `dirty`

Presentation: Present the items as one labelled submission unit with associated validation and form actions. Submission MUST use the declared action and MUST NOT bypass field validation.

### `list`

Ordered collection.

Required fields: `id`, `kind` = `list`, `items`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `selection_mode`, `empty`, `estimated_count`, `pagination`, `reorderable`

Presentation: Present items in their declared order using native list semantics. Selection, reordering, pagination and the declared empty state MUST remain operable without relying on visual position.

### `optionBar`

Page-level command surface.

Required fields: `id`, `kind` = `optionBar`, `options`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Present page-level commands in a consistent, keyboard-reachable command area. Commands MAY move into an overflow presentation, but MUST NOT disappear.

### `grid`

Collection optimized for visual scanning.

Required fields: `id`, `kind` = `grid`, `items`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `selection_mode`

Presentation: Use a spatial grid when it improves scanning and available space permits it. Reading and focus order MUST remain logical, with a list presentation as the default fallback.

Fallback: list.

### `table`

Row and column data.

Required fields: `id`, `kind` = `table`, `columns`, `rows`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `caption`, `sortable`, `filterable`, `selection_mode`, `pagination`

Presentation: Expose caption, column headers and row-to-column relationships. Narrow presentations SHOULD reflow, scroll or use the declared list or summary fallback without losing those relationships.

Fallback: list or summary.

### `tree`

Hierarchical data.

Required fields: `id`, `kind` = `tree`, `nodes`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `expanded_node_ids`, `selection_mode`

Presentation: Present an expandable hierarchy that exposes level, expanded state and selection. Keyboard viewers SHOULD follow native tree navigation conventions.

### `tabs`

Peer pages or views.

Required fields: `id`, `kind` = `tabs`, `tabs`, `selected`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Present peer views as a tab list with one selected tab and associated panel. Selection, focus movement and panel identity MUST be exposed to assistive technology.

### `toolbar`

Group of local actions.

Required fields: `id`, `kind` = `toolbar`, `actions`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `error`, `validation`, `fallback`, `metadata`

Presentation: Group related local actions in a labelled toolbar. Action order MUST be stable and every command MUST remain reachable by keyboard or equivalent input.

### `menu`

Command or local navigation menu.

Required fields: `id`, `kind` = `menu`, `items`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `trigger`, `selection_mode`

Presentation: Present commands or local navigation using native menu semantics when modal menu behavior is appropriate. Focus, dismissal and selection MUST follow host conventions.

### `breadcrumb`

Navigation path.

Required fields: `id`, `kind` = `breadcrumb`, `items`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Present an ordered navigation path. The current destination SHOULD be identified and normally SHOULD NOT be exposed as a redundant navigation action.

## 5.2 Content

### `text`

Plain read-only text or heading.

Required fields: `id`, `kind` = `text`, `text`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `text_role`, `selectable`

Presentation: Map text_role to the closest semantic text role, including a real heading when requested. Visual size alone MUST NOT be used to imply semantics.

### `valueDisplay`

Prominent time, duration, metric or measurement.

Required fields: `id`, `kind` = `valueDisplay`, one of `text` or `value`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `unit`, `source`, `format`

Presentation: Give the value suitable prominence while keeping its label, unit, source and live-update behavior available. A spoken representation MUST include enough context to understand the value.

### `richText`

Limited semantic formatted content.

Required fields: `id`, `kind` = `richText`, `content`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `format`, `language`

Presentation: Render only the declared limited semantic format. Publisher HTML, script, style and unsafe links MUST NOT be executed or passed through.

### `codeBlock`

Code, logs or command output.

Required fields: `id`, `kind` = `codeBlock`, `text`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `language`, `copy_action`

Presentation: Preserve whitespace and expose the content as code or preformatted output. Long lines MUST remain readable through wrapping, scrolling or an equivalent presentation, and copy_action SHOULD be offered when declared.

### `link`

Navigation to a resource.

Required fields: `id`, `kind` = `link`, `label`, `href`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `rel`, `type`, `external`, `download`

Presentation: Present as a focusable navigation link that activates inside the current LUMUI client unless policy or the external flag requires otherwise. Keyboard activation MUST work without a modifier key.

### `quote`

Quotation with optional attribution.

Required fields: `id`, `kind` = `quote`, `text`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `attribution`, `cite`, `language`

Presentation: Present as a quotation distinct from surrounding prose and associate attribution and citation when supplied. Assistive output MUST identify the quoted content before its attribution.

### `image`

Single image.

Required fields: `id`, `kind` = `image`, `source`, `alt`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `caption`, `intrinsic_aspect_ratio`, `decorative`, `variants`

Presentation: Render the best supported source or variant while respecting intrinsic aspect ratio. Meaningful images MUST expose alt text; decorative images MUST be omitted from accessibility output; sensitive images MUST follow privacy policy.

### `figure`

Self-contained content with caption.

Required fields: `id`, `kind` = `figure`, `content`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `caption`, `credit`, `source_link`

Presentation: Keep content, caption, credit and source link as one semantic figure. Caption and credit MUST remain associated when the layout changes.

### `imageCollection`

Related images.

Required fields: `id`, `kind` = `imageCollection`, `images`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `selection_mode`, `current_index`, `caption`

Presentation: Present an accessible gallery, carousel or sequential collection with stable item order and current position. Every image and selection action MUST be reachable without pointer-only gestures.

Fallback: sequential image or captions.

### `icon`

Small symbolic image.

Required fields: `id`, `kind` = `icon`, one of `symbol` or `source`, `meaning`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `decorative`

Presentation: Render the registered symbol or image without changing its meaning. Non-decorative icons MUST expose meaning and MUST NOT be the sole carrier of an action label or state.

### `badge`

Compact count or state label.

Required fields: `id`, `kind` = `badge`, `label`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `tone`

Presentation: Present the label and optional value as a compact annotation. Tone or color MUST NOT be the only indication of the badge state.

### `status`

Semantic state display.

Required fields: `id`, `kind` = `status`, `label`, `state`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `state_description`, `tone`

Presentation: Expose label, state and state description together. Tone MAY reinforce the state but MUST NOT replace text, and live announcements MUST use the declared live policy.

### `chart`

Data visualization.

Required fields: `id`, `kind` = `chart`, `chart_type`, `data`, `summary`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `table_fallback`, `axes`, `series`

Presentation: Provide the visual chart together with its summary and a data table or equivalent accessible fallback. Color, shape and position MUST NOT be the only way to distinguish series.

Fallback: table or summary.

### `clock`

Current time or declared time value.

Required fields: `id`, `kind` = `clock`, `label`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `timezone`

Presentation: Present the declared value, or the viewer's current time when value is absent, as a clock. Analog, digital and spoken forms are equivalent when the label, timezone and exact time remain available. Live updates MUST NOT disrupt focus or cause excessive announcements.

Fallback: valueDisplay.

## 5.3 Action And Choice

### `button`

User-triggered operation.

Required fields: `id`, `kind` = `button`, `label`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `confirmation`, `icon`

Presentation: Use a native command control with a visible label, focus indication and declared action. Confirmation is viewer-owned and MUST follow the action policy.

### `toggle`

Immediate binary setting.

Required fields: `id`, `kind` = `toggle`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Use a switch or equivalent immediate binary control and announce both label and current state. Activation MUST update only through the declared action.

### `checkOption`

Boolean membership in a set.

Required fields: `id`, `kind` = `checkOption`, `label`, `value`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Use a checkbox-like membership control, normally grouped with related options. Checked, disabled and descriptive states MUST be programmatically available.

### `imageOption`

Selectable option with image or swatch.

Required fields: `id`, `kind` = `imageOption`, `label`, `value`, `source`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `selected`

Presentation: Present a selectable tile or swatch with its text label and selected state. The image or color MUST NOT be the only way to identify the option.

### `detailOption`

Selectable row with details and trailing value.

Required fields: `id`, `kind` = `detailOption`, `label`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `text`, `selected`

Presentation: Present a selectable row containing its label, supporting description and trailing text or value. The complete target MUST remain understandable without depending on column position.

### `checkBox`

Boolean form selection.

Required fields: `id`, `kind` = `checkBox`, `label`, `value`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Use a native form checkbox with associated label, required state and validation. Checked state MUST be available visually and programmatically.

### `radioGroup`

Visible one-of-many selection.

Required fields: `id`, `kind` = `radioGroup`, `label`, `options`, `value`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Present the options as one labelled, single-selection group. Native radio-group focus and arrow-key behavior SHOULD be used when available.

### `choice`

General one-of-many selection.

Required fields: `id`, `kind` = `choice`, `label`, `options`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Present one-of-many selection as radios, a picker or another native single-selection control appropriate to the context. The selected value and declared action MUST remain explicit.

### `multiSelect`

Many-of-many selection.

Required fields: `id`, `kind` = `multiSelect`, `label`, `options`, `values`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min_selected`, `max_selected`

Presentation: Present a labelled multiple-selection control with each option and selected state exposed. Minimum and maximum selection constraints MUST be communicated before they block an action.

### `comboBox`

Filter plus constrained selection.

Required fields: `id`, `kind` = `comboBox`, `label`, `options`, `value`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `editable`, `preview`, `filter_mode`, `allow_empty`

Presentation: Use native combobox semantics with a text or selection control and associated option list. Preview MUST NOT commit a value, and editable behavior MUST remain constrained by the declared policy.

### `slider`

Numeric range control.

Required fields: `id`, `kind` = `slider`, `label`, `value`, `min`, `max`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `step`, `unit`, `display_value`, `marks`

Presentation: Use a range control that exposes label, current value, minimum, maximum, step and unit. Keyboard increments and a stepper or numeric-input fallback MUST be available.

Fallback: stepper or numeric input.

### `stepper`

Increment and decrement numeric value.

Required fields: `id`, `kind` = `stepper`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min`, `max`, `step`, `unit`

Presentation: Present a numeric value with increment and decrement commands and, when possible, direct entry. Boundary commands MUST become unavailable at declared limits.

### `rating`

Ordinal rating input.

Required fields: `id`, `kind` = `rating`, `label`, `value`, `max`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min`, `allow_half`

Presentation: Present an ordinal scale whose values have understandable labels. Stars or other symbols MAY be used visually, but the value and range MUST be available without interpreting those symbols.

## 5.4 Input

### `textField`

Single-line text entry.

Required fields: `id`, `kind` = `textField`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `placeholder`, `content_type`, `keyboard`, `autocomplete`, `min_length`, `max_length`, `pattern`, `spellcheck`, `autocapitalize`

Presentation: Use a labelled single-line text input with help, placeholder and validation associated programmatically. Keyboard and content hints MUST NOT replace validation.

### `textArea`

Multi-line text entry.

Required fields: `id`, `kind` = `textArea`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `placeholder`, `content_extent`, `max_length`, `spellcheck`

Presentation: Use a labelled multiline input. content_extent MAY influence initial size, but the viewer MUST recompose or scroll rather than clip entered content.

### `passwordField`

Secret text entry.

Required fields: `id`, `kind` = `passwordField`, `label`, `value_present`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `actions`, `error`, `validation`, `fallback`, `metadata`, `placeholder`, `min_length`, `max_length`, `rules`, `allow_reveal`, `password_manager`, `autocomplete`

Forbidden fields: `value`, `default_value`.

Presentation: Use a secret-entry control whose default presentation masks every entered character. Entered characters MUST remain viewer-owned transient input and MAY be sent only through the declared action's validated input. Outside an explicitly active reveal state, actual characters MUST NOT be exposed through visual or accessibility output; visual viewers conventionally substitute dots or asterisks. Actual characters MUST never enter surface state, logs, history or previews. A reveal control MAY appear only when allow_reveal is true, MUST require an explicit temporary action, MUST announce its state and MUST return to masked presentation.

### `searchField`

Search query entry.

Required fields: `id`, `kind` = `searchField`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `placeholder`, `suggestions`, `submit_action`, `clear_action`, `debounce_ms`, `result_count`

Presentation: Use a search input with reachable submit and clear actions and an associated suggestions presentation. Debounced changes MUST not steal focus, and result_count SHOULD be announced without excessive repetition.

### `numberField`

Numeric entry.

Required fields: `id`, `kind` = `numberField`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min`, `max`, `step`, `unit`, `precision`, `format`, `allow_negative`

Presentation: Use a locale-appropriate numeric input while preserving the unambiguous numeric value. Limits, precision, step, unit and validation MUST be exposed before submission.

### `dateField`

Date entry.

Required fields: `id`, `kind` = `dateField`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min`, `max`, `calendar`, `format`

Presentation: Use a native date picker or an accessible segmented equivalent. The displayed format MAY be localized, but the submitted value and minimum and maximum constraints MUST remain unambiguous.

### `timeField`

Time entry.

Required fields: `id`, `kind` = `timeField`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min`, `max`, `format`, `step_minutes`

Presentation: Use a native time picker or accessible segmented equivalent. Format and step_minutes MUST be reflected in entry and validation.

### `dateTimeField`

Combined date and time entry.

Required fields: `id`, `kind` = `dateTimeField`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min`, `max`, `timezone`

Presentation: Present date and time as one associated value or two clearly linked controls. The timezone MUST be visible or otherwise available whenever it affects interpretation.

### `dateRangeField`

Date range entry.

Required fields: `id`, `kind` = `dateRangeField`, `label`, `start`, `end`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min`, `max`, `allow_open_end`

Presentation: Present start and end as one labelled range with their relationship and validation exposed. An open end MAY be accepted only when allow_open_end permits it.

### `colorField`

Color as domain data.

Required fields: `id`, `kind` = `colorField`, `label`, `value`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `format`, `palette`, `allow_custom`

Presentation: Provide a color picker or palette together with a textual value or name. Color MUST NOT be the sole means of identifying the current choice.

### `otpField`

One-time code entry.

Required fields: `id`, `kind` = `otpField`, `label`, `length`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `actions`, `error`, `validation`, `fallback`, `metadata`, `value_present`, `content_type`, `auto_submit`

Forbidden fields: `value`, `default_value`.

Presentation: Present a one-time-code input with the expected length and current position understandable. Codes MUST remain viewer-owned transient input and MAY be sent only through the declared action's validated input. They MUST NOT be published, persisted or exposed in logs, history or previews, and auto submission MUST be announced before it occurs.

## 5.5 Feedback

### `progress`

Task progress.

Required fields: `id`, `kind` = `progress`, `label`, one of `value` or `indeterminate`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `min`, `max`, `unit`

Presentation: Use a determinate progress indicator when value is supplied and an activity presentation when indeterminate is true. Label and progress state MUST be available without relying on animation.

### `meter`

Bounded measurement.

Required fields: `id`, `kind` = `meter`, `label`, `value`, `min`, `max`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `low`, `high`, `optimum`, `unit`

Presentation: Use a bounded measurement that exposes value, minimum, maximum, thresholds and optimum. Color MAY reinforce ranges but MUST NOT be their only expression.

### `activity`

Indeterminate activity.

Required fields: `id`, `kind` = `activity`, `label`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Present ongoing indeterminate work with a readable status label. Reduced-motion preferences MUST replace unnecessary animation while preserving the activity state.

### `alert`

Important message.

Required fields: `id`, `kind` = `alert`, `title`, `message`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `severity`

Presentation: Present a persistent, prominent message with severity and actions. Live announcement urgency MUST follow the declared live value and MUST not cause repeated interruptions.

### `toast`

Temporary low-impact message.

Required fields: `id`, `kind` = `toast`, `message`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `duration_ms`

Presentation: Present a non-modal, temporary message without moving focus. It MUST remain available long enough to perceive and operate its action and MUST NOT carry critical information only.

### `error`

User-visible error state.

Required fields: `id`, `kind` = `error`, `title`, `message`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `severity`, `correlation_id`

Presentation: Present a persistent error heading and message, followed by recovery actions when supplied. Related invalid fields SHOULD be linked, and correlation_id SHOULD remain available for support without dominating the message.

### `emptyState`

No-content state.

Required fields: `id`, `kind` = `emptyState`, `title`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `message`, `illustration`

Presentation: Present an explicit no-content state instead of a blank region. The title, message, illustration and available next actions MUST remain associated.

## 5.6 Media And Capability

### `audio`

Embedded audio content.

Required fields: `id`, `kind` = `audio`, `label`, `source`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `transcript`, `duration_ms`, `preload`, `download`

Presentation: Present native or equivalent play, pause, seek and volume controls when playback is supported, with transcript and download fallbacks when declared. Controls MUST be keyboard operable.

Fallback: transcript or download.

### `audioPlayer`

Audio playback session.

Required fields: `id`, `kind` = `audioPlayer`, `label`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `artist`, `album`, `artwork`, `duration_ms`, `position_ms`, `state`, `source`

Presentation: Present a recognizable playback session with play or pause, stop, seek position, duration and relevant metadata. State changes MUST be announced and all declared actions MUST remain reachable.

### `video`

Embedded video content.

Required fields: `id`, `kind` = `video`, `title`, `source`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `poster`, `captions`, `transcript`, `audio_description`, `intrinsic_aspect_ratio`, `variants`

Presentation: Present video with native or equivalent playback controls, captions, transcript and audio description when supplied. Aspect ratio MUST be preserved and the declared poster, transcript or link fallback MUST remain usable.

Fallback: poster, transcript or link.

### `videoPlayer`

Video playback session.

Required fields: `id`, `kind` = `videoPlayer`, `session_id`, `title`, one of `source` or `session`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `captions`, `audio_description`, `poster`, `duration_ms`, `position_ms`, `state`

Presentation: Present the active playback session with play or pause, stop, seek, elapsed and total time, captions and fullscreen or equivalent viewing when supported. Session state MUST remain synchronized with declared actions.

### `calendar`

App-supplied date grid.

Required fields: `id`, `kind` = `calendar`, `children`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `options`

Presentation: Present the supplied dates using an accessible calendar grid when supported, including date labels, selection and keyboard movement. A chronological date list MUST be available as fallback.

Fallback: date list.

### `mediaPicker`

System-mediated media selection.

Required fields: `id`, `kind` = `mediaPicker`, `label`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `media_types`, `selection_mode`

Presentation: Open a trusted system-mediated media picker and explain the requested media types and selection mode. Publisher content MUST NOT imitate trusted permission or picker chrome.

### `map`

Geospatial display or selection.

Required fields: `id`, `kind` = `map`, `mode`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `center`, `markers`, `route`, `current_location`

Presentation: Present geographic content with keyboard-accessible controls and a textual list or route summary. Location, markers and selection MUST remain usable without seeing or manipulating the map.

Fallback: navigationSummary or location list.

### `navigation`

Route guidance.

Required fields: `id`, `kind` = `navigation`, `destination`, `route_summary`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `current_step`, `distance_remaining`, `eta`, `maneuvers`

Presentation: Present the current maneuver, remaining distance, estimated arrival and route context with concise visual, spoken or haptic output. Guidance MUST NOT rely on the map alone.

### `locationPicker`

System-mediated location selection.

Required fields: `id`, `kind` = `locationPicker`, `label`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `mode`, `requires_capabilities`

Presentation: Use a trusted permission and location-selection surface, show the selected location textually and require an explicit action before sharing it.

### `dialer`

Phone keypad or call control.

Required fields: `id`, `kind` = `dialer`, `mode`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `number`, `contact`, `call_state`

Presentation: Present a recognizable telephone keypad or call-control surface with large operable targets and announced call state. Starting or ending a call MUST use explicit declared actions.

### `contactPicker`

System-mediated contact selection.

Required fields: `id`, `kind` = `contactPicker`, `label`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Use a trusted system-mediated contact selector and disclose the requested result before selection. Only the fields required by the action SHOULD be returned.

### `filePicker`

System-mediated file selection.

Required fields: `id`, `kind` = `filePicker`, `label`, `action`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `actions`, `error`, `validation`, `fallback`, `metadata`, `accept`, `selection_mode`, `intent`, `target`, `copy_policy`

Presentation: Use a trusted system file selector that enforces accepted types, selection mode and intent. Selected file names and any copy or upload consequence MUST be shown before action submission.

### `dialog`

Modal app decision.

Required fields: `id`, `kind` = `dialog`, `title`, one of `message` or `items`, `actions`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `error`, `validation`, `fallback`, `metadata`

Presentation: Present as a modal decision with title, message or items and clearly ordered actions. Focus MUST enter the dialog, remain within it while modal and return to the invoking control when dismissed.

### `notification`

Content submitted to a trusted notification surface.

Required fields: `id`, `kind` = `notification`, `title`, `body`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`, `category`, `expires_at`

Presentation: Submit content to a trusted notification surface with accessible actions and priority. Sensitive content MUST be redacted according to lock-screen and user privacy policy.

### `graphic`

Sandboxed specialized graphics container.

Required fields: `id`, `kind` = `graphic`, `label`, `purpose`, `renderer`, `source`, `fallback`.

Optional fields: `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `metadata`, `state_schema`, `integrity`, `capabilities`

Presentation: Run only a registered sandboxed renderer with declared capabilities and integrity policy. An equivalent semantic fallback MUST be available before the specialized graphic is exposed.

## 5.7 Preview

### `preview`

Displays a component in its own preview area.

`content` contains the component to display. Preview does not load another surface or change the render context.

Required fields: `id`, `kind` = `preview`, `content`.

Optional fields: `label`, `description`, `help`, `enabled`, `visible`, `priority`, `sensitive`, `live`, `required`, `readonly`, `value`, `default_value`, `action`, `actions`, `error`, `validation`, `fallback`, `metadata`

Presentation: Present the contained component in a distinct preview area. The renderer MAY choose a preview-specific layout and MUST preserve the contained component's semantics, state and actions.

Fallback: render content without the preview layout.

## 6. Conformance

A component renderer conforms when it:

1. accepts only fields permitted by the catalog and schema;
2. preserves the purpose, values, state, relationships and actions defined here;
3. implements every `MUST` rule for the rendered kind;
4. provides the declared or documented fallback when specialized presentation is unavailable;
5. remains operable with keyboard, pointer, touch, assistive technology and other supported input and output modes;
6. applies user accessibility and privacy policy before final composition.

Visual similarity is not required. Semantic and behavioral equivalence is required.
