'use client'

import { Fragment } from 'react'

import Box from '@mui/material/Box'
import Divider from '@mui/material/Divider'
import Link from '@mui/material/Link'
import Typography from '@mui/material/Typography'

/**
 * Minimal CommonMark-subset renderer for assistant replies.
 *
 * Why hand-rolled instead of react-markdown: the PMT frontend ships no markdown dependency,
 * and the agent's output is deliberately narrow ("keep answers concise and reference entities
 * by id and title" - AiAgentOrchestrator's system prompt). This covers what the model
 * actually emits - paragraphs, headings, bold/italic, inline code, fenced code, lists,
 * blockquotes, rules and links - in ~150 lines instead of a ~40 kB dependency.
 *
 * Security: every node is produced as a React element and model output is only ever passed as
 * a *child*, never as HTML - there is no dangerouslySetInnerHTML anywhere in this file - so
 * raw HTML or a <script> tag in a reply renders as literal text. Link targets are additionally
 * restricted to http(s), mailto and app-relative paths so a `javascript:` URL cannot be
 * clicked into existence.
 *
 * Known gap: pipe tables are not parsed and degrade to plain paragraph text.
 */

const FENCE = /^\s*```(\S+)?\s*$/
const FENCE_END = /^\s*```\s*$/
const HEADING = /^(#{1,6})\s+(.*)$/
const RULE = /^\s*(-{3,}|\*{3,}|_{3,})\s*$/
const QUOTE = /^\s*>\s?/
const BULLET = /^\s*[-*+]\s+/
const ORDERED = /^\s*\d+[.)]\s+/

const isBlockStart = line =>
  FENCE.test(line) || HEADING.test(line) || RULE.test(line) || QUOTE.test(line) || BULLET.test(line) || ORDERED.test(line)

const parseBlocks = source => {
  const lines = String(source ?? '')
    .replace(/\r\n/g, '\n')
    .split('\n')

  const blocks = []
  let index = 0

  while (index < lines.length) {
    const line = lines[index]

    if (!line.trim()) {
      index++
      continue
    }

    const fence = FENCE.exec(line)

    if (fence) {
      const body = []

      index++

      while (index < lines.length && !FENCE_END.test(lines[index])) {
        body.push(lines[index])
        index++
      }

      // Skips the closing fence, or lands past the end for an unterminated block - a reply
      // truncated mid-code-block must still render.
      index++
      blocks.push({ type: 'code', content: body.join('\n') })
      continue
    }

    const heading = HEADING.exec(line)

    if (heading) {
      blocks.push({ type: 'heading', level: heading[1].length, content: heading[2] })
      index++
      continue
    }

    if (RULE.test(line)) {
      blocks.push({ type: 'rule' })
      index++
      continue
    }

    if (QUOTE.test(line)) {
      const body = []

      while (index < lines.length && QUOTE.test(lines[index])) {
        body.push(lines[index].replace(QUOTE, ''))
        index++
      }

      blocks.push({ type: 'quote', content: body.join('\n') })
      continue
    }

    if (BULLET.test(line) || ORDERED.test(line)) {
      const ordered = ORDERED.test(line) && !BULLET.test(line)
      const marker = ordered ? ORDERED : BULLET
      const items = []

      while (index < lines.length && marker.test(lines[index])) {
        items.push(lines[index].replace(marker, ''))
        index++
      }

      blocks.push({ type: 'list', ordered, items })
      continue
    }

    const paragraph = []

    while (index < lines.length && lines[index].trim() && !isBlockStart(lines[index])) {
      paragraph.push(lines[index].trim())
      index++
    }

    // Defensive: an unmatched block-start would otherwise spin here forever.
    if (!paragraph.length) {
      paragraph.push(lines[index].trim())
      index++
    }

    blocks.push({ type: 'paragraph', content: paragraph.join('\n') })
  }

  return blocks
}

const safeHref = href => {
  const value = String(href ?? '').trim()

  return /^(https?:\/\/|mailto:|\/)/i.test(value) ? value : null
}

// Ordered by precedence: code spans win over emphasis so `**literal**` inside backticks stays
// literal. None of these are global regexes, so there is no shared lastIndex to reset.
// Emphasis and link labels recurse through renderInline so `**bold `code`**` nests correctly;
// the captured text always excludes its delimiters, so the recursion strictly shrinks.
const INLINE_RULES = [
  { pattern: /`([^`]+)`/, render: (match, key) => <code key={key}>{match[1]}</code> },
  {
    pattern: /\[([^\]]+)\]\(([^)\s]+)\)/,
    render: (match, key, renderChildren) => {
      const href = safeHref(match[2])

      // A `javascript:` or otherwise unrecognised scheme degrades to the link text.
      if (!href) return <Fragment key={key}>{renderChildren(match[1], key)}</Fragment>

      const external = /^https?:\/\//i.test(href)

      return (
        <Link
          key={key}
          href={href}
          target={external ? '_blank' : undefined}
          rel={external ? 'noopener noreferrer' : undefined}
          underline='hover'
        >
          {renderChildren(match[1], key)}
        </Link>
      )
    }
  },
  {
    pattern: /(\*\*|__)(?=\S)([\s\S]*?\S)\1/,
    render: (match, key, renderChildren) => <strong key={key}>{renderChildren(match[2], key)}</strong>
  },
  {
    pattern: /(^|[\s(])([*_])(?=\S)([^*_]*?\S)\2/,
    render: (match, key, renderChildren) => (
      <Fragment key={key}>
        {match[1]}
        <em>{renderChildren(match[3], key)}</em>
      </Fragment>
    )
  }
]

const renderInline = (text, keyPrefix) => {
  const nodes = []
  let rest = String(text ?? '')
  let cursor = 0

  while (rest) {
    let best = null

    for (const rule of INLINE_RULES) {
      const match = rule.pattern.exec(rest)

      if (match && (!best || match.index < best.match.index)) best = { rule, match }
    }

    if (!best) {
      nodes.push(rest)
      break
    }

    if (best.match.index > 0) nodes.push(rest.slice(0, best.match.index))

    const key = `${keyPrefix}-${cursor}`

    nodes.push(best.rule.render(best.match, key, (child, childKey) => renderInline(child, `${childKey}-c`)))
    rest = rest.slice(best.match.index + best.match[0].length)
    cursor++
  }

  return nodes
}

/** Renders inline content, preserving the soft line breaks inside a paragraph. */
const renderLines = (text, keyPrefix) => {
  const lines = String(text ?? '').split('\n')

  return lines.map((line, position) => (
    <Fragment key={`${keyPrefix}-line-${position}`}>
      {renderInline(line, `${keyPrefix}-line-${position}`)}
      {position < lines.length - 1 ? <br /> : null}
    </Fragment>
  ))
}

const HEADING_VARIANTS = ['h6', 'subtitle1', 'subtitle2', 'subtitle2', 'subtitle2', 'subtitle2']

const renderBlock = (block, key) => {
  switch (block.type) {
    case 'heading':
      return (
        <Typography key={key} variant={HEADING_VARIANTS[block.level - 1]} sx={{ fontWeight: 600 }}>
          {renderInline(block.content, key)}
        </Typography>
      )

    case 'code':
      // Rendered as a bare <pre> rather than <pre><code>: globals.css styles every <code>
      // element as an inline chip (info colour, tinted background), which is wrong for a block.
      return (
        <Box
          key={key}
          component='pre'
          sx={{
            m: 0,
            p: 3,
            borderRadius: 1,
            bgcolor: 'action.hover',
            border: 1,
            borderColor: 'divider',
            overflowX: 'auto',
            fontFamily: 'monospace',
            fontSize: '0.8125rem',
            lineHeight: 1.7
          }}
        >
          {block.content}
        </Box>
      )

    case 'quote':
      return (
        <Box
          key={key}
          sx={{ borderInlineStart: '3px solid', borderColor: 'primary.main', paddingInlineStart: 3, py: 0.5 }}
        >
          <Typography variant='body2' color='text.secondary'>
            {renderLines(block.content, key)}
          </Typography>
        </Box>
      )

    case 'list':
      // Deliberately not a flex container: flex items lose their list markers in some
      // engines, so item spacing comes from a margin instead.
      return (
        <Box
          key={key}
          component={block.ordered ? 'ol' : 'ul'}
          sx={{ m: 0, paddingInlineStart: 5, '& > li + li': { marginBlockStart: 1 } }}
        >
          {block.items.map((item, position) => (
            <Typography key={`${key}-item-${position}`} component='li' variant='body2'>
              {renderInline(item, `${key}-item-${position}`)}
            </Typography>
          ))}
        </Box>
      )

    case 'rule':
      return <Divider key={key} />

    default:
      return (
        <Typography key={key} variant='body2'>
          {renderLines(block.content, key)}
        </Typography>
      )
  }
}

const AiMarkdown = ({ content }) => {
  const blocks = parseBlocks(content)

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2, overflowWrap: 'anywhere' }}>
      {blocks.map((block, position) => renderBlock(block, `block-${position}`))}
    </Box>
  )
}

export default AiMarkdown
