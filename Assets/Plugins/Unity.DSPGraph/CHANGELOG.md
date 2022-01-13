# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.1.0-preview.18] - 2021-02-10
### Improvements
- Refresh documentation
### Changes
- Bump burst dependency to 1.4.4
- Minimum Unity version is now 2020.2

## [0.1.0-preview.17] - 2020-11-16
### Changes
- Bump burst dependency to 1.4.1

## [0.1.0-preview.16] - 2020-10-31
### Changes
- Bump version number after CI workarounds

## [0.1.0-preview.15] - 2020-10-21
### Changes
- Bump burst dependency to 1.3.7

## [0.1.0-preview.14] - 2020-10-05
### Fixes
- Fix incorrect exceptions for graphs whose buffer size isn't a multiple of the channel count
- Don't show test and sample code in API documentation

## [0.1.0-preview.13] - 2020-09-10
### Changes
- Update dependencies
### Fixes
- Fix uninitialized buffer access

## [0.1.0-preview.12] - 2020-02-20
### Changes
- Migrate sample buffers to one buffer per channel
- Update com.unity.media.utilities dependency to preview.4
### Improvements
- Remove some allocations caused by boxing
### Fixes
- Fix crash when using dspgraph and exiting unity via script

## [0.1.0-preview.11] - 2019-12-02
### Improvements
- Fix playback in PlayClip sample

## [0.1.0-preview.10] - 2019-11-29
### Improvements
- Add a set of some simple samples
- Update com.unity.media.utilities to preview.3

### Fixes
- Fix leak in node job data

## [0.1.0-preview.9] - 2019-11-15
### Fixes
- Update com.unity.media.utilities dependency to publically-available preview.2

## [0.1.0-preview.8] - 2019-11-06
### Improvements
- Extract collections into their own package
- Add DefaultDspGraphDriver implementation
- Improve granularity of profiler markers
### Fixes
- Fixes and workarounds for il2cpp compilation
- Fix embedded image in manual

## [0.1.0-preview.7] - 2019-09-30
### Improvements
- Improve synchronization of unattached subgraphs
### Fixes and improvements
- Reenable bursting of output jobs
- Fix safety handle error when building player

## [0.1.0-preview.6] - 2019-08-26
### Improvements
- Migrate DSPGraph implementation to C#
- Add optional support for executing subgraphs with no outputs

## [0.1.0-preview.5] - 2019-08-26
### Fixes for megacity
Fix edge case interpolation problems in megacity

## [0.1.0-preview.4] - 2019-07-09
### Bump burst version
Fixes build report compilation error in burst package

## [0.1.0-preview.3] - 2019-06-13
### Add documentation for ExecutionMode
*Add documentation for ExecutionMode*

## [0.1.0-preview.2] - 2019-06-12
### Add output job documentation
*Add output job documentation*

## [0.1.0-preview.1] - 2019-02-12
### This is the first release of *DSPGraph Audio Framework \<com.unity.audio.dspgraph\>*.
*Initial preview release*
