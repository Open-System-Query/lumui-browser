# Viewer guide

Status: being rebuilt around the working web viewer  
LUMUI surface: 1.0  
LUMUI web protocol: 1.0  

The viewer guide is being rewritten as a practical companion to the
specification. It will explain the product through real tasks instead of
internal architecture.

## The working model

An application describes its content, state and available actions. A viewer
checks that description and presents it for the current device and person.

The same contract can be implemented in any language or framework. A web
implementation can include the HTML renderer to provide complete browser pages
alongside structured output. Native and specialised viewers map the same
components to their own controls.

The `preview` component contains one component for presentation in a dedicated
preview area. It is separate from viewer features that simulate another device
or presentation context.

## What the guide will cover

1. Open an application through discovery or a direct surface.
2. Validate the document before it reaches the renderer.
3. Present it as a standard, step-by-step, focused or glanceable view.
4. Adapt it for everyday, shared, wearable, appliance, print and alternative
   outputs.
5. Combine text, colour, motion, focus and reading preferences.
6. Send actions back safely and display the returned state.
7. Inspect source, structure, requests, performance, problems and accessibility.

## Available now

- `/viewer/`: client-side web viewer
- `/demo/`: guided visitor check-in demonstration
- `/browser/`: native browser overview
- `/specification/`: core compatibility rules
- `/components/`: component catalogue

The previous design baseline is preserved as
`viewer-specification.archive.md` while the new guide is prepared.
