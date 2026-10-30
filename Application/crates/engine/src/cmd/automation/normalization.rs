//! Normalization retains each scalar's original offset so edits never flatten
//! different character styles or cross inline objects and line breaks.
use inkstone_doc::{CharProps, Paragraph};

pub(super) struct Edit {
    pub start: usize,
    pub end: usize,
    pub text: String,
    pub props: CharProps,
}

pub(super) fn plan(para: &Paragraph, rules: &[String]) -> (String, Vec<Edit>) {
    let mut chars: Vec<_> = para.text.char_indices().collect();
    for rule in rules {
        match rule.as_str() {
            "trimTrailingSpaces" => {
                let mut trailing = true;
                let mut keep = vec![true; chars.len()];
                for (index, (_, c)) in chars.iter().enumerate().rev() {
                    if matches!(c, '\n' | '\r') {
                        trailing = true;
                    } else if trailing && matches!(c, ' ' | '\t') {
                        keep[index] = false;
                    } else {
                        trailing = false;
                    }
                }
                let mut index = 0;
                chars.retain(|_| {
                    let retained = keep[index];
                    index += 1;
                    retained
                });
            }
            "halfSpace" => {
                // This also handles نمی: it ends with the same می prefix.
                for index in 2..chars.len() {
                    if chars[index - 2].1 == 'م' && chars[index - 1].1 == 'ی' && chars[index].1 == ' ' {
                        chars[index].1 = '\u{200c}';
                    }
                }
            }
            _ => {
                for (_, c) in &mut chars {
                    *c = match (rule.as_str(), *c) {
                        ("persianLetters", 'ي') => 'ی',
                        ("persianLetters", 'ك') => 'ک',
                        ("persianDigits", value) => value
                            .to_digit(10)
                            .or_else(|| "٠١٢٣٤٥٦٧٨٩".chars().position(|n| n == value).map(|n| n as u32))
                            .and_then(|n| char::from_u32('۰' as u32 + n))
                            .unwrap_or(value),
                        ("latinDigits", value) => "۰۱۲۳۴۵۶۷۸۹"
                            .chars()
                            .position(|n| n == value)
                            .or_else(|| "٠١٢٣٤٥٦٧٨٩".chars().position(|n| n == value))
                            .and_then(|n| char::from_u32('0' as u32 + n as u32))
                            .unwrap_or(value),
                        (_, value) => value,
                    };
                }
            }
        }
    }
    let after: String = chars.iter().map(|(_, c)| c).collect();
    let mut normalized = chars.into_iter().peekable();
    let mut runs = para.run_ranges().peekable();
    let mut edits = Vec::new();
    let mut segment: Option<Edit> = None;
    for (offset, original) in para.text.char_indices() {
        while runs.peek().is_some_and(|(range, _)| range.end <= offset) {
            runs.next();
        }
        let props = runs.peek().map(|(_, props)| *props).unwrap_or(&para.mark);
        let value = if normalized.peek().is_some_and(|(at, _)| *at == offset) { normalized.next().map(|(_, c)| c) } else { None };
        let boundary = original == '\u{fffc}' || original.is_control() || original == '\u{2029}';
        if boundary || segment.as_ref().is_some_and(|edit| edit.props != *props) {
            finish(&para.text, segment.take(), &mut edits);
        }
        if boundary {
            if value != Some(original) {
                edits.push(Edit { start: offset, end: offset + original.len_utf8(), text: value.into_iter().collect(), props: props.clone() });
            }
            continue;
        }
        let edit = segment.get_or_insert_with(|| Edit { start: offset, end: offset, text: String::new(), props: props.clone() });
        edit.end = offset + original.len_utf8();
        if let Some(c) = value {
            edit.text.push(c);
        }
    }
    finish(&para.text, segment, &mut edits);
    (after, edits)
}

fn finish(before: &str, segment: Option<Edit>, edits: &mut Vec<Edit>) {
    let Some(mut edit) = segment else { return };
    let Some(old) = before.get(edit.start..edit.end) else { return };
    if old == edit.text {
        return;
    }
    let prefix: usize = old.chars().zip(edit.text.chars()).take_while(|(a, b)| a == b).map(|(c, _)| c.len_utf8()).sum();
    let old_tail = &old[prefix..];
    let new_tail = &edit.text[prefix..];
    let suffix: usize = old_tail.chars().rev().zip(new_tail.chars().rev()).take_while(|(a, b)| a == b).map(|(c, _)| c.len_utf8()).sum();
    edit.start += prefix;
    edit.end -= suffix;
    edit.text = edit.text[prefix..edit.text.len() - suffix].to_string();
    edits.push(edit);
}
