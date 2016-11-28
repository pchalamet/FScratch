

[<RequireQualifiedAccess>]
type AgentMessage =
    | Message of string





type Agent =
    abstract member OnMessage: string -> unit
    default this.OnMessage (msg : string) = ()

    member this.Post message =
        match message with
        | AgentMessage.Message msg -> this.OnMessage msg




type IHub =
    abstract Register: name : string
                    -> agent : Agent
                    -> unit

    abstract Send: name : string
                -> message : string
                -> unit

    abstract Publish: message : string
                   -> unit



[<RequireQualifiedAccess>]
type HubMessage =
    | Start of AsyncReplyChannel<unit>
    | Register of (string * Agent)
    | Send of string * string
    | Publish of string
    | Exit


let private queueProcessor (inbox : MailboxProcessor<HubMessage>) =
    async {
        let! start = inbox.Receive()
        let reply = match start with
                    | HubMessage.Start reply -> reply
                    | _ -> failwith "Expecting Start message"

        let rec messageLoop (agents : Map<string, Agent>) =
            async {
                let! msg = inbox.Receive()
                match msg with
                | HubMessage.Start arc -> failwith "Unexpected message Start"
                | HubMessage.Register kvp -> let newAgents = agents.Add kvp
                                             return! messageLoop newAgents
                | HubMessage.Send (name,msg) -> let agent = agents.[name]
                                                AgentMessage.Message msg |> agent.Post
                | HubMessage.Publish msg -> agents |> Map.iter (fun _ agent -> AgentMessage.Message msg |> agent.Post)
                | HubMessage.Exit -> reply.Reply()
            }
        do! messageLoop Map.empty
    }


type Hub() = class end
with
    let mailbox : MailboxProcessor<HubMessage> = new MailboxProcessor<HubMessage>(queueProcessor)

    interface IHub with
        member this.Register (name : string) (agent : Agent) =
            HubMessage.Register (name, agent) |> mailbox.Post 

        member this.Send (name : string) (message : string) =
            HubMessage.Send (name, message) |> mailbox.Post

        member this.Publish (message : string) =
            HubMessage.Publish message |> mailbox.Post
            
    member this.WaitCompletion () =
        mailbox.Start()
        mailbox.PostAndAsyncReply(HubMessage.Start) |> Async.RunSynchronously


[<EntryPoint>]
let main argv = 
    let hub = Hub()
    hub.WaitCompletion()

    0
