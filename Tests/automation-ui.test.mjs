import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createJobSubmission,mergeJob,updateJob} from '../Front/src/automationState.js';
import {formatDate,formatNumber} from '../Front/src/workspaceFormat.js';
test('workspace dates and numbers follow the selected site locale',()=>{
  const value=Date.UTC(2026,9,30,12,0);
  assert.equal(formatDate(value,'fa-IR',true),new Intl.DateTimeFormat('fa-IR',{dateStyle:'medium'}).format(value));
  assert.notEqual(formatDate(value,'fa-IR',true),formatDate(value,'en-US',true));
  assert.equal(formatNumber(1234,'fa-IR'),new Intl.NumberFormat('fa-IR').format(1234));
});
test('a lost job acknowledgement retries the same idempotency key; changed inputs get a new key',async()=>{
  const sent=[];let fail=true;
  const submit=createJobSubmission(async input=>{sent.push(input);if(fail)throw Error('connection lost');return {id:'job'};});
  const input={kind:'export',source:{id:'source',version:1},format:'pdf'};
  await assert.rejects(submit(input));fail=false;await submit(input);
  assert.equal(sent[0].idempotencyKey,sent[1].idempotencyKey);
  await submit(input);assert.notEqual(sent[1].idempotencyKey,sent[2].idempotencyKey);
  fail=true;await assert.rejects(submit(input));await assert.rejects(submit({...input,format:'docx'}));assert.notEqual(sent[3].idempotencyKey,sent[4].idempotencyKey);
});
test('stale job polls cannot overwrite an acknowledged cancellation',()=>{
  const jobs=[{id:'a',version:4,status:'cancelled'},{id:'b',version:1,status:'queued'}];
  assert.deepEqual(updateJob(jobs,{id:'a',version:3,status:'running'}),jobs);
  assert.equal(updateJob(jobs,{id:'b',version:2,status:'running'})[1].status,'running');
});
test('job details reject late polls and keep their report across list refreshes',()=>{
  const detail={id:'a',version:4,status:'succeeded',report:{exported:'pdf'}};
  assert.deepEqual(mergeJob(detail,{id:'a',version:3,status:'running',report:{}}),detail);
  assert.deepEqual(mergeJob(detail,{id:'a',version:4,status:'succeeded'}),detail);
  assert.deepEqual(mergeJob(detail,{id:'b',version:5,status:'cancelled'}),detail);
  assert.equal(mergeJob(detail,{id:'a',version:5,status:'cancelled',report:{}}).status,'cancelled');
});
