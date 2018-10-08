module MiniBlazor.Render

open Microsoft.AspNetCore.Blazor
open Microsoft.AspNetCore.Blazor.RenderTree

let rec renderNode (builder: RenderTreeBuilder) sequence node =
    match node with
    | Empty -> sequence
    | Text text ->
        builder.AddContent(sequence, text)
        sequence + 1
    | Concat nodes ->
        List.fold (renderNode builder) sequence nodes
    | Elt (name, attrs, children) ->
        builder.OpenElement(sequence, name)
        let sequence = sequence + 1
        let sequence = Seq.fold (renderAttr builder) sequence attrs
        let sequence = List.fold (renderNode builder) sequence children
        builder.CloseElement()
        sequence
    | Component (comp, i, attrs, children) ->
        let initSequence = sequence
        builder.OpenComponent(sequence, comp)
        let sequence = sequence + 1
        let sequence = Seq.fold (renderAttr builder) sequence attrs
        if not (List.isEmpty children) then
            let frag = RenderFragment(fun builder ->
                let sequence = Seq.fold (renderNode builder) sequence children
                assert (sequence = initSequence + i.length))
            builder.AddAttribute(sequence, "ChildContent", frag)
        builder.CloseComponent()
        initSequence + i.length

and renderAttr (builder: RenderTreeBuilder) sequence (name, value) =
    builder.AddAttribute(sequence, name, value)
    sequence + 1