# External expression watches

## Why

External debugging needs recurring expression inspection alongside Locals and Immediate.

## What Changes

Implement #1180, part of #696: a separate external watch list retained for the IDE session, evaluated
in the selected stopped frame with DAP watch context. Interpreter watch definitions remain separate.
Hover and break-condition watches are outside this slice.
