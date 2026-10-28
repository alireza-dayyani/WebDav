use super::{Session, json};
use inkstone_doc::{CharProps, Document, Paragraph, para_block};

#[test]
fn batch_is_atomic_and_search_crosses_runs() {
    let mut para = Paragraph::with_text("Customer علی Customer", CharProps::default());
    para.format(0, 4, &|c| c.bold = Some(true)).unwrap();
    let mut doc = Document::new();
    doc.body = vec![para_block(para)];
    let mut s = Session::new(doc);
    let before = s.doc.clone();
    assert!(
        s.run(
            "document.batch",
            &json!({"operations":[{"type":"replaceText","find":"Customer","replacement":"Client","expectedMatches":2},{"type":"unknown"}]})
        )
        .is_err()
    );
    assert_eq!(s.doc, before);
    let found = s.run("document.query", &json!({"action":"search","text":"Customer"})).unwrap();
    assert_eq!(found["count"], 2);
    s.run("document.batch", &json!({"operations":[{"type":"replaceText","find":"Customer","replacement":"Client","expectedMatches":2}]})).unwrap();
    assert_eq!(s.doc.plain_text(inkstone_doc::StoryRef::Body), "Client علی Client");
}

#[test]
fn rejects_bad_unicode_offsets_and_missing_matches() {
    let mut s = Session::new(Document::from_text("علی"));
    let before = s.doc.clone();
    assert!(s.run("document.batch", &json!({"operations":[{"type":"replaceRange","target":{"ref":"b.0","start":1,"end":2},"text":"x"}]})).is_err());
    assert!(s.run("document.batch", &json!({"operations":[{"type":"replaceText","find":"absent","replacement":"x","expectedMatches":1}]})).is_err());
    assert_eq!(s.doc, before);
}

#[test]
fn malformed_guards_and_format_values_are_rejected() {
    let mut s = Session::new(Document::from_text("Name"));
    let before = s.doc.clone();
    for operation in [
        json!({"type":"replaceText","find":"Name","replacement":"Wrong","expectedMatches":-1}),
        json!({"type":"replaceText","find":"Name","replacement":"Wrong","matchCase":"true"}),
        json!({"type":"replaceRange","target":{"ref":"b.0","start":-1},"text":"Wrong"}),
        json!({"type":"format","target":{"ref":"b.0"},"values":{"font":123}}),
    ] {
        assert!(s.run("document.batch", &json!({"operations":[operation]})).is_err());
        assert_eq!(s.doc, before);
    }
}

#[test]
fn typed_controls_enforce_types_and_locks() {
    let mut s = Session::new(Document::new());
    s.run("developer.checkBox", &json!({"tag":"Approved"})).unwrap();
    s.run("document.batch", &json!({"operations":[{"type":"fillControls","values":{"Approved":true}}]})).unwrap();
    assert_eq!(s.run("document.query", &json!({"action":"controls"})).unwrap()["items"][0]["checked"], true);
    let before = s.doc.clone();
    assert!(s.run("document.batch", &json!({"operations":[{"type":"fillControls","values":{"Approved":"yes"}}]})).is_err());
    assert_eq!(s.doc, before);
    s.run("developer.properties", &json!({"control":"Approved","lockEdit":true})).unwrap();
    assert!(s.run("document.batch", &json!({"operations":[{"type":"fillControls","values":{"Approved":false}}]})).is_err());
}

#[test]
fn tables_and_formatting_use_addressable_references() {
    let mut s = Session::new(Document::new());
    s.run("insert.table", &json!({"rows":2,"cols":2})).unwrap();
    let result = s.run("document.query", &json!({"action":"tables"})).unwrap();
    let reference = result["items"][0]["ref"].clone();
    s.run(
        "document.batch",
        &json!({"operations":[{"type":"updateTable","ref":reference,"cells":[{"row":0,"column":1,"text":"نام"}],"appendRows":[["A","B"]]}]}),
    )
    .unwrap();
    let result = s.run("document.query", &json!({"action":"tables"})).unwrap();
    assert_eq!(result["items"][0]["rows"], 3);
    assert_eq!(result["items"][0]["cells"][0][1]["text"], "نام");
}

#[test]
fn normalization_preview_is_read_only() {
    let mut s = Session::new(Document::from_text("علي 123 ك مي رود"));
    let before = s.doc.clone();
    let report = s.run("document.query", &json!({"action":"normalization","rules":["persianLetters","persianDigits","halfSpace"]})).unwrap();
    assert_eq!(s.doc, before);
    assert!(!report["changes"].as_array().unwrap().is_empty());
    s.run("document.batch", &json!({"operations":[{"type":"normalize","rules":["persianLetters","persianDigits"]}]})).unwrap();
    assert!(s.doc.plain_text(inkstone_doc::StoryRef::Body).contains("علی ۱۲۳ ک"));
}

#[test]
fn document_validation_returns_supported_checks() {
    let mut s = Session::new(Document::from_text("Hello {{Name}}"));
    let result = s.run("document.query", &json!({"action":"validate"})).unwrap();
    assert_eq!(result["placeholders"].as_array().unwrap().len(), 1);
}

#[test]
fn repeating_sections_materialize_typed_records_and_remain_reusable() {
    let mut s = Session::new(Document::new());
    s.run("developer.plainText", &json!({"tag":"Name"})).unwrap();
    s.run("select.all", &json!({})).unwrap();
    s.run("developer.repeatingSection", &json!({"tag":"Rows"})).unwrap();
    s.run("document.batch", &json!({"operations":[{"type":"fillControls","values":{"Rows":[{"Name":"Alice"},{"Name":"Bob"}]}}]})).unwrap();
    let text = s.doc.plain_text(inkstone_doc::StoryRef::Body);
    assert!(text.contains("Alice") && text.contains("Bob"), "{text}");
    s.run("document.batch", &json!({"operations":[{"type":"fillControls","values":{"Rows":[{"Name":"Carol"}]}}]})).unwrap();
    let text = s.doc.plain_text(inkstone_doc::StoryRef::Body);
    assert!(text.contains("Carol") && !text.contains("Alice") && !text.contains("Bob"), "{text}");
}
