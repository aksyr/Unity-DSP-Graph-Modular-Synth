# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [0.1.0-preview.14] - 2021-10-07
### Changes
- Bump test-framework dependency to 1.1.27

## [0.1.0-preview.13] - 2021-04-26
### Changes
- Bump burst dependency to 1.5.3

## [0.1.0-preview.12] - 2021-04-19
### Changes
- Bump burst dependency to 1.5.2

## [0.1.0-preview.11] - 2021-03-22
### Improvements
- Add OwnedAtomicQueue for self-management of payload storage
- AtomicFreeList.Acquire now returns whether the node was allocated or reused
### Changes
- Bump burst dependency to 1.5.0

## [0.1.0-preview.10] - 2021-02-10
### Improvements
- Leverage unmanaged generics to remove "description" helpers
- Refresh API documentation
### Changes
- Bump burst dependency to 1.4.4
- Minimum Unity version is now 2020.2

## [0.1.0-preview.9] - 2020-11-16
### Changes
- Bump burst dependency to 1.4.1

## [0.1.0-preview.8] - 2020-10-31
### Changes
- Bump version number after CI workarounds

## [0.1.0-preview.7] - 2020-10-21
### Changes
- Bump burst dependency to 1.3.7

## [0.1.0-preview.6] - 2020-10-05
### Fixes
- Fixed AtomicQueue for 32bit platforms
- Don't show test code in API documentation

## [0.1.0-preview.5] - 2020-08-04
### Fixes
- Fixed index validation in GrowableBuffer being off by one

## [0.1.0-preview.4] - 2020-02-02
### Changes
- The minimum Unity version is now 2019.3
### Improvements
- GrowableBuffer: Add AddRange
- Utility: Add helper methods for interleaving/deinterleaving audio buffers
### Fixes
- Fix unsynchronized read in AtomicFreeList

## [0.1.0-preview.3] - 2019-11-28
### Improvements
- Improve robustness of AtomicQueue

## [0.1.0-preview.2] - 2019-11-13
### Improvements
- Add AtomicQueue.TryDequeue()

## [0.1.0-preview.1] - 2019-11-11
### This is the first release of *Unity Media Utilities \<com.unity.media.utilities\>*.
*Initial preview release*
