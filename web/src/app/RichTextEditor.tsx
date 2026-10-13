import {
  Button,
  Field,
  Input,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  ToolbarToggleButton,
  Tooltip,
  makeStyles,
  tokens,
} from '@fluentui/react-components'
import {
  BracesVariableRegular,
  LinkRegular,
  TextBoldRegular,
  TextBulletListLtrRegular,
  TextItalicRegular,
  TextNumberListLtrRegular,
  TextUnderlineRegular,
} from '@fluentui/react-icons'
import { EditorContent, useEditor, useEditorState } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import { useEffect, useState } from 'react'
import type { MergeField } from '../api/templates'

const useStyles = makeStyles({
  wrap: {
    border: `1px solid ${tokens.colorNeutralStroke1}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  toolbar: { borderBottom: `1px solid ${tokens.colorNeutralStroke2}`, flexWrap: 'wrap' },
  content: {
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    minHeight: '220px',
    maxHeight: '520px',
    overflowY: 'auto',
    '& .ProseMirror': { outline: 'none', minHeight: '200px' },
    '& .ProseMirror p': { margin: `0 0 ${tokens.spacingVerticalS}` },
  },
  link: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'end' },
  // The field list is long; it scrolls instead of running off the screen.
  fields: { maxHeight: 'min(360px, 60vh)', overflowY: 'auto' },
})

export interface RichTextEditorProps {
  /** Accessible name of the editing area. */
  label: string
  value: string
  onChange: (html: string) => void
  /** Offered in the "Insert field" menu; inserted as {{key}}. */
  mergeFields?: readonly MergeField[]
}

/**
 * ET-01: rich-text editing (bold, italic, underline, lists, links) with merge fields inserted as {{field}}.
 * The HTML is cleaned again on the server, so the editor's output is never trusted as-is.
 */
export function RichTextEditor({ label, value, onChange, mergeFields = [] }: RichTextEditorProps) {
  const styles = useStyles()
  const editor = useEditor({
    extensions: [StarterKit.configure({ heading: { levels: [2, 3] }, codeBlock: false, code: false, link: { openOnClick: false, protocols: ['mailto'] } })],
    content: value,
    editorProps: { attributes: { role: 'textbox', 'aria-multiline': 'true', 'aria-label': label } },
    onUpdate: ({ editor: e }) => onChange(e.getHTML()),
  })
  const state = useEditorState({
    editor,
    selector: ({ editor: e }) => ({
      bold: e?.isActive('bold') ?? false,
      italic: e?.isActive('italic') ?? false,
      underline: e?.isActive('underline') ?? false,
      bullets: e?.isActive('bulletList') ?? false,
      numbers: e?.isActive('orderedList') ?? false,
    }),
  })

  // Content set from outside (a loaded template or preview) replaces the editor's content.
  useEffect(() => {
    if (editor && value !== editor.getHTML()) editor.commands.setContent(value, { emitUpdate: false })
  }, [editor, value])

  const checked = Object.entries(state ?? {})
    .filter(([, on]) => on)
    .map(([name]) => name)

  return (
    <div className={styles.wrap}>
      <Toolbar aria-label={`${label} formatting`} size="small" className={styles.toolbar} checkedValues={{ format: checked }}>
        <ToolbarToggleButton aria-label="Bold" icon={<TextBoldRegular />} name="format" value="bold" onClick={() => editor?.chain().focus().toggleBold().run()} />
        <ToolbarToggleButton aria-label="Italic" icon={<TextItalicRegular />} name="format" value="italic" onClick={() => editor?.chain().focus().toggleItalic().run()} />
        <ToolbarToggleButton
          aria-label="Underline"
          icon={<TextUnderlineRegular />}
          name="format"
          value="underline"
          onClick={() => editor?.chain().focus().toggleUnderline().run()}
        />
        <ToolbarDivider />
        <ToolbarToggleButton
          aria-label="Bulleted list"
          icon={<TextBulletListLtrRegular />}
          name="format"
          value="bullets"
          onClick={() => editor?.chain().focus().toggleBulletList().run()}
        />
        <ToolbarToggleButton
          aria-label="Numbered list"
          icon={<TextNumberListLtrRegular />}
          name="format"
          value="numbers"
          onClick={() => editor?.chain().focus().toggleOrderedList().run()}
        />
        <LinkButton
          onApply={(href) =>
            href
              ? editor?.chain().focus().extendMarkRange('link').setLink({ href }).run()
              : editor?.chain().focus().extendMarkRange('link').unsetLink().run()
          }
          current={(editor?.getAttributes('link').href as string | undefined) ?? ''}
        />
        {mergeFields.length > 0 && (
          <>
            <ToolbarDivider />
            <Menu>
              <MenuTrigger disableButtonEnhancement>
                <ToolbarButton icon={<BracesVariableRegular />}>Insert field</ToolbarButton>
              </MenuTrigger>
              <MenuPopover className={styles.fields}>
                <MenuList>
                  {mergeFields.map((f) => (
                    <Tooltip key={f.key} content={f.description} relationship="description" positioning="after">
                      <MenuItem onClick={() => editor?.chain().focus().insertContent(`{{${f.key}}}`).run()}>{f.label}</MenuItem>
                    </Tooltip>
                  ))}
                </MenuList>
              </MenuPopover>
            </Menu>
          </>
        )}
      </Toolbar>
      <EditorContent editor={editor} className={styles.content} />
    </div>
  )
}

function LinkButton({ current, onApply }: { current: string; onApply: (href: string) => void }) {
  const styles = useStyles()
  const [open, setOpen] = useState(false)
  const [href, setHref] = useState('')
  return (
    <Popover
      open={open}
      onOpenChange={(_, d) => {
        setOpen(d.open)
        if (d.open) setHref(current)
      }}
      trapFocus
    >
      <PopoverTrigger disableButtonEnhancement>
        <ToolbarButton aria-label="Link" icon={<LinkRegular />} />
      </PopoverTrigger>
      <PopoverSurface>
        <form
          className={styles.link}
          onSubmit={(e) => {
            e.preventDefault()
            onApply(href.trim())
            setOpen(false)
          }}
        >
          <Field label="Link address" hint="https://, mailto: or a merge field such as {{requisition_link}}">
            <Input value={href} onChange={(_, d) => setHref(d.value)} />
          </Field>
          <Button type="submit" appearance="primary">
            {href.trim() ? 'Apply' : 'Remove link'}
          </Button>
        </form>
      </PopoverSurface>
    </Popover>
  )
}
