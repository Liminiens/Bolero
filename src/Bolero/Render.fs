// $begin{copyright}
//
// This file is part of Bolero
//
// Copyright (c) 2018 IntelliFactory and contributors
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}

module Bolero.Render

open System
open System.Collections.Generic
open FSharp.Reflection

#if !DEBUG_RENDERER
open Microsoft.AspNetCore.Blazor
open Microsoft.AspNetCore.Blazor.RenderTree
#else
open System.IO

type BlazorTreeBuilder = Microsoft.AspNetCore.Blazor.RenderTree.RenderTreeBuilder
type BlazorFragment = Microsoft.AspNetCore.Blazor.RenderFragment

type RenderTreeBuilder(b: BlazorTreeBuilder, indent: int, out: TextWriter) =
    let mutable indent = indent

    let write fmt =
            Printf.kprintf (fun s ->
                out.Write(Array.create (indent * 2) ' ')
                out.Write(s)
                out.Flush()
            ) fmt

    let writen fmt =
            Printf.kprintf (fun s ->
                out.Write(Array.create (indent * 2) ' ')
                out.WriteLine(s)
                out.Flush()
            ) fmt

    member this.AddContent(sequence: int, text: string) =
        if not (isNull b) then b.AddContent(sequence, text)
        writen "Text %i %s" sequence text

    member this.AddContent(sequence: int, frag: RenderFragment) =
        if not (isNull b) then b.AddContent(sequence, frag.Frag)
        write "Frag %i\n%s" sequence (frag.Show(indent + 1))

    member this.AddMarkupContent(sequence: int, markup: string) =
        if not (isNull b) then b.AddMarkupContent(sequence, markup)
        writen "Markup %i %s" sequence markup

    member this.OpenElement(sequence: int, name: string) =
        if not (isNull b) then b.OpenElement(sequence, name)
        writen "OpenElt %i %s" sequence name
        indent <- indent + 1

    member this.CloseElement() =
        indent <- indent - 1
        if not (isNull b) then b.CloseElement()
        writen "CloseElt"

    member this.OpenComponent(sequence: int, ty: Type) =
        if not (isNull b) then b.OpenComponent(sequence, ty)
        writen "OpenComp %i %s" sequence ty.FullName
        indent <- indent + 1

    member this.CloseComponent() =
        indent <- indent - 1
        if not (isNull b) then b.CloseComponent()
        writen "CloseComp"

    member this.AddAttribute(sequence: int, name: string, value: obj) =
        match value with
        | :? RenderFragment as f ->
            if not (isNull b) then b.AddAttribute(sequence, name, f.Frag)
            writen "Attr %i %s\n%s" sequence name (f.Show(indent + 1))
        | value ->
            if not (isNull b) then b.AddAttribute(sequence, name, value)
            writen "Attr %i %s %A" sequence name value

and RenderFragment(f: RenderTreeBuilder -> unit) =
    member this.Frag : BlazorFragment =
        BlazorFragment(fun b -> f(RenderTreeBuilder(b, 0, TextWriter.Null)))
    member this.Show(indent) =
        use w = new StringWriter()
        f(RenderTreeBuilder(null, indent, w))
        w.ToString()
#endif

/// Render `node` into `builder` at `sequence` number.
let rec renderNode (builder: RenderTreeBuilder) (matchCache: Type -> int * (obj -> int)) sequence node =
    match node with
    | Empty -> sequence
    | Text text ->
        builder.AddContent(sequence, text)
        sequence + 1
    | RawHtml html ->
        builder.AddMarkupContent(sequence, html)
        sequence + 1
    | Concat nodes ->
        List.fold (renderNode builder matchCache) sequence nodes
    | Cond (cond, node) ->
        builder.AddContent(sequence + (if cond then 1 else 0),
            RenderFragment(fun tb -> renderNode tb matchCache 0 node |> ignore))
        sequence + 2
    | Match (unionType, value, node) ->
        let caseCount, getMatchedCase = matchCache unionType
        let matchedCase = getMatchedCase value
        builder.AddContent(sequence + matchedCase,
            RenderFragment(fun tb -> renderNode tb matchCache 0 node |> ignore))
        sequence + caseCount
    | ForEach nodes ->
        builder.AddContent(sequence,
            RenderFragment(fun tb -> List.iter (renderNode tb matchCache 0 >> ignore) nodes))
        sequence + 1
    | Elt (name, attrs, children) ->
        builder.OpenElement(sequence, name)
        let sequence = sequence + 1
        let sequence = Seq.fold (renderAttr builder) sequence attrs
        let sequence = List.fold (renderNode builder matchCache) sequence children
        builder.CloseElement()
        sequence
    | Component (comp, attrs, children) ->
        builder.OpenComponent(sequence, comp)
        let sequence = sequence + 1
        let sequence = Seq.fold (renderAttr builder) sequence attrs
        let hasChildren = not (List.isEmpty children)
        if hasChildren then
            let frag = RenderFragment(fun builder ->
                builder.AddContent(sequence + 1, RenderFragment(fun builder ->
                    Seq.fold (renderNode builder matchCache) 0 children
                    |> ignore)))
            builder.AddAttribute(sequence, "ChildContent", frag)
        builder.CloseComponent()
        sequence + (if hasChildren then 2 else 0)

/// Render an attribute with `name` and `value` into `builder` at `sequence` number.
and renderAttr builder sequence = function
    | Attr (name, value) ->
        builder.AddAttribute(sequence, name, value)
        sequence + 1
    | Attrs attrs ->
        List.fold (renderAttr builder) sequence attrs

let RenderNode builder (matchCache: Dictionary<Type, _>) node =
    let getMatchParams (ty: Type) =
        match matchCache.TryGetValue(ty) with
        | true, x -> x
        | false, _ ->
            let caseCount = FSharpType.GetUnionCases(ty, true).Length
            let r = FSharpValue.PreComputeUnionTagReader(ty)
            let v = (caseCount, r)
            matchCache.[ty] <- v
            v
#if DEBUG_RENDERER
    let builder = RenderTreeBuilder(builder, 0, stdout)
#endif
    renderNode builder getMatchParams 0 node
    |> ignore
