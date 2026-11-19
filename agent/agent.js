// agent.js - his agent: what the person asks in the chat, done on this computer. An Express app made a device by Onsay's
// port for Node (onsay-ports/node), which Desktop starts (src/Core/Core.cs), so every route here and the shell are
// offered, checked at the door and logged raw like any device's. It thinks with Onsay's AI, and works with two tools: the
// shell, this computer's own main call, and the lingo search, for anything this computer can't do by itself.
//
// 1. POST /agent/ask   -> { prompt, messages, show } in: the conversation so far, the prompt the newest of it. Out, a line
//                         of JSON at a time: { step } as each part starts, then { text, messages }, his reply and the
//                         messages this added, or { error }. He goes on until the job is done, up to TURNS model turns

import express from 'express'
import { onsay, describe } from 'onsay'

const TURNS = 60          // the most model turns one prompt may take
const KEPT = 20_000       // the most characters of a tool's answer he reads

const GUIDE = `You are Onsay, an agent working on the person's Windows computer. Do what they ask, all the way, then say in
a sentence or two what you did. Never stop halfway to ask whether to go on.

Your tools:
- shell: runs a PowerShell command on this computer and gives back what it printed. Use it for everything this computer
  can do: files, programs, settings, the web through Invoke-WebRequest. Check what a command did before you say it worked.
- search: Onsay's lingo search. Say in words what you want done, with any values, and it finds a way somewhere else that
  does it, runs it and gives back its answer: another of the person's devices, or something someone offers. Use it only
  for what this computer can't do by itself.

Plain words in replies, short, no jargon. Never show the person an error: say what you will try instead, then try it.`

const TOOLS = [
  {
    type: 'function',
    function: {
      name: 'shell',
      description: 'Runs a PowerShell command on this computer and gives back what it printed and its exit code.',
      parameters: { type: 'object', properties: { command: { type: 'string', description: 'the command, as typed at the prompt' } }, required: ['command'] },
    },
  },
  {
    type: 'function',
    function: {
      name: 'search',
      description: "Onsay's lingo search: finds a way somewhere else that does what you say, runs it, and gives back its answer.",
      parameters: {
        type: 'object',
        properties: {
          words: { type: 'string', description: 'what you want done, in plain words' },
          values: { type: 'object', description: 'any inputs it needs, by name' },
        },
        required: ['words'],
      },
    },
  },
]

////////// FUNCTIONS //////////

const app = express()
const port = onsay(app)

// One tool call, run: the shell through this device's own route, so it is logged like any call, or the search.
const run = async (name, args) => {
  if (name === 'shell') {
    const res = await fetch(`http://127.0.0.1:${process.env.ONSAY_PORT ?? 6969}/shell`, {
      method: 'POST',
      headers: { 'content-type': 'application/json', authorization: `Bearer ${port.local}` },
      body: JSON.stringify({ command: String(args.command ?? '') }),
    })
    return res.json()
  }
  if (name === 'search') return { answer: await port.search(String(args.words ?? ''), args.values ?? {}) }
  return { error: `There's no tool called ${name}` }
}

// What a tool call is doing, as a step the person sees.
const stepOf = (name, args) => {
  if (name === 'shell') return `Running ${String(args.command ?? '').split('\n')[0].slice(0, 80)}`
  return `Asking Onsay: ${String(args.words ?? '').slice(0, 80)}`
}

// 1.
app.post('/agent/ask', describe({ does: 'Has Onsay do what was asked on this computer, all the way, and says what it did', takes: { type: 'object', properties: { prompt: { type: 'string' }, messages: { type: 'array' } }, required: ['prompt'] } }), async (req, res) => {
  res.setHeader('content-type', 'application/x-ndjson')
  const say = (line) => res.write(`${JSON.stringify(line)}\n`)
  const messages = [].concat(req.body?.messages ?? [])
  if (!messages.length) messages.push({ role: 'user', content: String(req.body?.prompt ?? '') })
  const added = []
  try {
    for (let turn = 0; turn < TURNS; turn++) {
      const got = await port.ai({ messages: [{ role: 'system', content: GUIDE }, ...messages], tools: TOOLS })
      const message = got.choices?.[0]?.message ?? { role: 'assistant', content: '' }
      messages.push(message)
      added.push(message)
      if (!message.tool_calls?.length) {
        say({ text: message.content ?? '', messages: added })
        return res.end()
      }
      for (const call of message.tool_calls) {
        let args = {}
        try {
          args = JSON.parse(call.function?.arguments || '{}')
        } catch {
          args = {}
        }
        say({ step: stepOf(call.function?.name, args) })
        const answer = await run(call.function?.name, args).catch((err) => ({ error: err.message }))
        const reply = { role: 'tool', tool_call_id: call.id, content: JSON.stringify(answer).slice(0, KEPT) }
        messages.push(reply)
        added.push(reply)
      }
    }
    say({ text: "That took more steps than I can take at once. Say go on and I'll pick up from here.", messages: added })
  } catch (err) {
    // The free month being over is said as it is, so the person knows what to do: anything else stays out of sight.
    if (err.status === 402) say({ text: err.message, messages: added })
    else say({ error: err.message })
  }
  res.end()
})
